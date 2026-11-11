using System.IO.Ports;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DeskBuddy.Core.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskBuddy.Core.Device;

/// <summary>Line-oriented byte pipe to the buddy: Bluetooth LE (<see cref="BleTransport"/>) or USB (<see cref="SerialTransport"/>).</summary>
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

/// <summary>Keeps the connection to the buddy alive and moves JSON lines in and out.</summary>
public sealed class DeviceLink(ConfigStore config, ILogger<DeviceLink> log, TimeProvider time,
    Func<DeviceSettings, IDeviceTransport>? transportFactory = null) : BackgroundService
{
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(35);

    private readonly Func<DeviceSettings, IDeviceTransport> _factory =
        transportFactory ?? (s => s.Connection == ConnectionKind.Usb
            ? new SerialTransport(s.PortName!, s.BaudRate)
            : new BleTransport(s.BluetoothName));
    private readonly Channel<string> _outbox = Channel.CreateBounded<string>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly LinkedList<LogLine> _log = new();
    private readonly Lock _logGate = new();
    private CancellationTokenSource? _sessionCts;
    private DeviceSettings? _activeSettings;

    public LinkStatus Status { get; private set; } = LinkStatus.Disconnected;
    public string? PortName { get; private set; }
    public string? Firmware { get; private set; }
    /// <summary>Screens the buddy is currently using (from its hello).</summary>
    public int? DeviceScreens { get; private set; }
    /// <summary>Whether a second screen answered on the buddy's second display address.</summary>
    public bool? SecondScreenDetected { get; private set; }
    /// <summary>Display type the buddy is using ("oled" / "lcd1602").</summary>
    public string? DeviceDisplay { get; private set; }
    /// <summary>Whether an OLED answered at boot.</summary>
    public bool? OledDetected { get; private set; }
    /// <summary>I2C address an LCD backpack answered at during boot (0 = none found).</summary>
    public int? LcdDetectedAddress { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastHeard { get; private set; }
    /// <summary>Link open AND the buddy answered recently.</summary>
    public bool Online => Status == LinkStatus.Connected && LastHeard is { } h && time.GetUtcNow() - h < SilenceTimeout;

    public event Action<DeviceEvent>? EventReceived;
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
    public void Reconnect() => _sessionCts?.Cancel();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        config.Changed += c =>
        {
            // Only a change to the connection itself warrants dropping the link.
            if (_activeSettings is { } a && (a.Connection != c.Device.Connection || a.BluetoothName != c.Device.BluetoothName
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
                log.LogInformation("Buddy link to {Target} failed: {Error}", Target(settings), ex.Message);
            }

            SetStatus(LinkStatus.Disconnected, Target(settings));
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); } catch (OperationCanceledException) { }
        }
    }

    private async Task RunSessionAsync(DeviceSettings settings, CancellationToken stoppingToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _sessionCts = session;
        _activeSettings = settings;
        SetStatus(LinkStatus.Connecting, Target(settings));

        using var transport = _factory(settings);
        await Task.Run(transport.Open, session.Token);
        while (_outbox.Reader.TryRead(out _)) { } // stale messages from a previous session
        LastError = null;
        Firmware = null;
        DeviceScreens = null;
        SecondScreenDetected = null;
        DeviceDisplay = null;
        OledDetected = null;
        LcdDetectedAddress = null;
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
            _sessionCts = null;
            _activeSettings = null;
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
                AddLog(false, line);
                if (DeviceEvent.TryParse(line) is not { } evt) continue;

                LastHeard = time.GetUtcNow();
                if (evt.Type == "hello")
                {
                    Firmware = evt.Raw["fw"]?.GetValue<string>();
                    DeviceScreens = evt.Raw["screens"]?.GetValue<int>();
                    SecondScreenDetected = evt.Raw["disp2"]?.GetValue<bool>();
                    DeviceDisplay = evt.Raw["display"]?.GetValue<string>();
                    OledDetected = evt.Raw["oled"]?.GetValue<bool>();
                    LcdDetectedAddress = evt.Raw["lcd"]?.GetValue<int>();
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

    /// <summary>What we connect to: a serial port, or "Bluetooth: DeskBuddy".</summary>
    private static string? Target(DeviceSettings s) => s.Connection == ConnectionKind.Usb
        ? s.PortName
        : string.IsNullOrWhiteSpace(s.BluetoothName) ? null : $"Bluetooth: {s.BluetoothName}";

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
        void OnChange(DeskBuddyConfig _) => tcs.TrySetResult();
        config.Changed += OnChange;
        try { await tcs.Task.WaitAsync(ct); }
        catch (OperationCanceledException) { }
        finally { config.Changed -= OnChange; }
    }
}
