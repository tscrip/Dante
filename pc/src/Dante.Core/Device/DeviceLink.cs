using System.IO.Ports;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Dante.Core.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dante.Core.Device;

/// <summary>Line-oriented byte pipe to DANTE: Bluetooth LE (<see cref="BleTransport"/>) or USB (<see cref="SerialTransport"/>).</summary>
public interface IDeviceTransport : IDisposable
{
    void Open();
    bool IsOpen { get; }
    /// <summary>Blocks up to the read timeout; returns null on timeout.</summary>
    string? ReadLine();
    void WriteLine(string line);
}

public sealed class SerialTransport(string portName, int baud) : IDeviceTransport
{
    private readonly SerialPort _port = new(portName, baud)
    {
        NewLine = "\n",
        ReadTimeout = 500,
        WriteTimeout = 2000,
        DtrEnable = false, // don't reset the ESP32(-S3) when we connect over USB
        RtsEnable = false,
    };

    public bool IsOpen => _port.IsOpen;
    public void Open() => _port.Open();

    public string? ReadLine()
    {
        try { return _port.ReadLine().TrimEnd('\r'); }
        catch (TimeoutException) { return null; }
    }

    public void WriteLine(string line) => _port.Write(line + "\n");
    public void Dispose() => _port.Dispose();

    /// <summary>Serial ports plus the USB device nodes .NET doesn't list on Linux/macOS.</summary>
    public static IReadOnlyList<string> ListPorts()
    {
        var ports = new SortedSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows() && Directory.Exists("/dev"))
        {
            foreach (var pattern in new[] { "cu.usb*", "ttyUSB*", "ttyACM*" })
                foreach (var f in Directory.EnumerateFiles("/dev", pattern))
                    ports.Add(f);
        }
        return [.. ports];
    }
}

public enum LinkStatus { Disconnected, Connecting, Connected }

public sealed record LogLine(DateTimeOffset At, bool Outgoing, string Text);

/// <summary>Keeps the connection to DANTE alive and moves JSON lines in and out.</summary>
public sealed class DeviceLink(ConfigStore config, ILogger<DeviceLink> log, TimeProvider time,
    Func<DeviceSettings, IDeviceTransport>? transportFactory = null) : BackgroundService
{
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(35);

    private readonly Func<DeviceSettings, IDeviceTransport> _factory =
        transportFactory ?? (s => s.Connection switch
        {
            ConnectionKind.Usb => new SerialTransport(s.PortName!, s.BaudRate),
            ConnectionKind.Simulator => new SimulatorTransport(SimulatorLocator.Find(s.SimulatorPath), s.SimulatedHardware),
            _ => new BleTransport(s.BluetoothName),
        });
    private readonly Channel<string> _outbox = Channel.CreateBounded<string>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly LinkedList<LogLine> _log = new();
    private readonly Lock _logGate = new();
    private CancellationTokenSource? _sessionCts;
    private CancellationTokenSource? _retryCts;
    private DeviceSettings? _activeSettings;

    public LinkStatus Status { get; private set; } = LinkStatus.Disconnected;
    public string? PortName { get; private set; }
    public string? Firmware { get; private set; }
    /// <summary>Display type DANTE is using ("oled" / "tft").</summary>
    public string? DeviceDisplay { get; private set; }
    /// <summary>Whether an OLED answered at boot.</summary>
    public bool? OledDetected { get; private set; }
    /// <summary>DANTE is driving the 3.5" colour TFT.</summary>
    public bool DeviceTft => DeviceDisplay == "tft";
    /// <summary>DANTE has a touch screen instead of buttons (the ESP32-3248S035C "yellow board"; firmware 3.6+).</summary>
    public bool TouchScreen { get; private set; }
    /// <summary>Latest reply to <see cref="DeviceProtocol.Diag"/>.</summary>
    public WiringReport? LastDiag { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastHeard { get; private set; }
    /// <summary>Link open AND DANTE answered recently.</summary>
    public bool Online => Status == LinkStatus.Connected && LastHeard is { } h && time.GetUtcNow() - h < SilenceTimeout;

    public event Action<DeviceEvent>? EventReceived;

    private const string SimFramePrefix = "{\"t\":\"sim.frame\"";
    /// <summary>Latest screen contents from the simulator (raw JSON line), when connected to it.</summary>
    public string? LastSimFrame { get; private set; }
    public event Action<string>? SimFrameReceived;

    /// <summary>Simulator only: press (down) or release one of the simulated DANTE's buttons.</summary>
    public void SimButton(string button, bool down) =>
        Send(new System.Text.Json.Nodes.JsonObject { ["t"] = "sim.btn", ["b"] = button, ["down"] = down });

    /// <summary>
    /// Simulator only (yellow board): what the "fingers" are doing on the touch screen. <paramref name="fingers"/>
    /// is 0 when nothing touches it; x, y are screen pixels (0-479, 0-319).
    /// </summary>
    public void SimTouch(int fingers, int x, int y) =>
        Send(new System.Text.Json.Nodes.JsonObject
        {
            ["t"] = "sim.touch", ["n"] = Math.Clamp(fingers, 0, 2), ["x"] = Math.Clamp(x, 0, 479), ["y"] = Math.Clamp(y, 0, 319),
        });
    public event Action? StatusChanged;

    public IReadOnlyList<LogLine> RecentLog()
    {
        lock (_logGate) return [.. _log];
    }

    public void Send(JsonObject message)
    {
        if (Status == LinkStatus.Connected) _outbox.Writer.TryWrite(message.ToJsonString());
    }

    /// <summary>Drop the current connection; the loop reconnects with the latest settings.</summary>
    public void Reconnect()
    {
        // Either ends the current connection, or cuts short the wait before the next attempt.
        foreach (var cts in new[] { _sessionCts, _retryCts })
        {
            try { cts?.Cancel(); }
            catch (ObjectDisposedException) { /* that session/wait just ended by itself */ }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        config.Changed += c =>
        {
            // Waiting to retry after a failed attempt: try the new settings right away.
            if (_activeSettings is null) { Reconnect(); return; }
            // Only a change to the connection itself warrants dropping the link.
            if (_activeSettings is { } a && (a.Connection != c.Device.Connection || a.BluetoothName != c.Device.BluetoothName
                    || a.SimulatedHardware != c.Device.SimulatedHardware || a.SimulatorPath != c.Device.SimulatorPath
                    || a.PortName != c.Device.PortName || a.BaudRate != c.Device.BaudRate || !c.Device.AutoConnect))
                Reconnect();
        };
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = config.Snapshot().Device;
            if (!settings.AutoConnect || string.IsNullOrWhiteSpace(Target(settings)))
            {
                SetStatus(LinkStatus.Disconnected, null);
                await WaitForChangeAsync(stoppingToken);
                continue;
            }

            try
            {
                await RunSessionAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // settings changed / manual reconnect
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                log.LogInformation("DANTE link to {Target} failed: {Error}", Target(settings), ex.Message);
            }

            SetStatus(LinkStatus.Disconnected, Target(settings));
            using (var retry = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                _retryCts = retry;
                try { await Task.Delay(TimeSpan.FromSeconds(3), retry.Token); } catch (OperationCanceledException) { }
                _retryCts = null;
            }
        }
    }

    private async Task RunSessionAsync(DeviceSettings settings, CancellationToken stoppingToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _sessionCts = session;
        _activeSettings = settings;
        try
        {
            await RunConnectedAsync(settings, session);
        }
        finally
        {
            // Also when connecting fails: nothing may keep using this (soon disposed) session.
            _sessionCts = null;
            _activeSettings = null;
        }
    }

    private async Task RunConnectedAsync(DeviceSettings settings, CancellationTokenSource session)
    {
        SetStatus(LinkStatus.Connecting, Target(settings));

        using var transport = _factory(settings);
        await Task.Run(transport.Open, session.Token);
        while (_outbox.Reader.TryRead(out _)) { } // stale messages from a previous session
        LastError = null;
        Firmware = null;
        DeviceDisplay = null;
        OledDetected = null;
        TouchScreen = false;
        LastSimFrame = null;
        SetStatus(LinkStatus.Connected, Target(settings));

        Write(transport, DeviceProtocol.Hello().ToJsonString());

        var reader = Task.Factory.StartNew(() => ReadLoop(transport, session), TaskCreationOptions.LongRunning);
        var lastPing = time.GetUtcNow();
        try
        {
            while (!session.IsCancellationRequested)
            {
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                tick.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    var line = await _outbox.Reader.ReadAsync(tick.Token);
                    Write(transport, line);
                }
                catch (OperationCanceledException) when (!session.IsCancellationRequested) { }

                if (time.GetUtcNow() - lastPing >= PingEvery)
                {
                    Write(transport, DeviceProtocol.Ping().ToJsonString());
                    lastPing = time.GetUtcNow();
                }
                if (reader.IsCompleted) await reader; // surfaces read errors
            }
        }
        finally
        {
            await session.CancelAsync();
            try { await reader; } catch { /* already reported */ }
        }
    }

    private void ReadLoop(IDeviceTransport transport, CancellationTokenSource session)
    {
        try
        {
            while (!session.IsCancellationRequested)
            {
                var line = transport.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith(SimFramePrefix, StringComparison.Ordinal))
                {
                    // Simulator screen contents: kept for the Simulator page, far too chatty for the log.
                    LastHeard = time.GetUtcNow();
                    LastSimFrame = line;
                    SimFrameReceived?.Invoke(line);
                    continue;
                }
                AddLog(false, line);
                if (DeviceEvent.TryParse(line) is not { } evt) continue;

                LastHeard = time.GetUtcNow();
                if (evt.Type == "diag")
                    LastDiag = new WiringReport(time.GetUtcNow(), evt.Raw["boot"]?.GetValue<string>() ?? "", evt.Raw["scan"]?.GetValue<string>() ?? "");
                if (evt.Type == "hello")
                {
                    Firmware = evt.Raw["fw"]?.GetValue<string>();
                    DeviceDisplay = evt.Raw["display"]?.GetValue<string>();
                    OledDetected = evt.Raw["oled"]?.GetValue<bool>();
                    TouchScreen = evt.Raw["touch"]?.GetValue<bool>() == true;   // absent on boards with buttons
                }
                StatusChanged?.Invoke();
                EventReceived?.Invoke(evt);
            }
        }
        catch (Exception ex) when (!session.IsCancellationRequested)
        {
            LastError = ex.Message;
            session.Cancel();
            throw;
        }
    }

    private void Write(IDeviceTransport transport, string line)
    {
        if (line.Contains("\"t\":\"ping\"") is false) AddLog(true, line);
        transport.WriteLine(line);
    }

    private void AddLog(bool outgoing, string text)
    {
        lock (_logGate)
        {
            _log.AddLast(new LogLine(time.GetUtcNow(), outgoing, text));
            while (_log.Count > 200) _log.RemoveFirst();
        }
    }

    /// <summary>What we connect to: a serial port, or "Bluetooth: DANTE".</summary>
    private static string? Target(DeviceSettings s) => s.Connection switch
    {
        ConnectionKind.Usb => s.PortName,
        ConnectionKind.Simulator => $"Simulator ({s.SimulatedHardware.Describe()})",
        _ => string.IsNullOrWhiteSpace(s.BluetoothName) ? null : $"Bluetooth: {s.BluetoothName}",
    };

    private void SetStatus(LinkStatus status, string? port)
    {
        if (Status == status && PortName == port) return;
        Status = status;
        PortName = port;
        StatusChanged?.Invoke();
    }

    private async Task WaitForChangeAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChange(DanteConfig _) => tcs.TrySetResult();
        config.Changed += OnChange;
        try { await tcs.Task.WaitAsync(ct); }
        catch (OperationCanceledException) { }
        finally { config.Changed -= OnChange; }
    }
}
