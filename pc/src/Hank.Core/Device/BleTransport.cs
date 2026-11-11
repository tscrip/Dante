using System.Collections.Concurrent;
using System.Text;
using InTheHand.Bluetooth;

namespace Hank.Core.Device;

/// <summary>
/// JSON lines over Bluetooth LE using the Nordic UART Service, which the ESP32-S3 firmware exposes
/// (the S3 has no Bluetooth Classic, so there's no serial-port-over-Bluetooth any more).
/// Works on Windows (WinRT) and Linux (BlueZ). On macOS use the USB cable.
/// </summary>
public sealed class BleTransport(string deviceName) : IDeviceTransport
{
    public static readonly Guid ServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    /// <summary>The PC writes here.</summary>
    public static readonly Guid RxUuid = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    /// <summary>HANK notifies here.</summary>
    public static readonly Guid TxUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

    private static readonly TimeSpan ScanTime = TimeSpan.FromSeconds(8);

    private readonly BlockingCollection<string> _lines = new();
    private readonly LineSplitter _splitter = new();
    private BluetoothDevice? _device;
    private GattCharacteristic? _rx;
    private GattCharacteristic? _tx;
    private int _chunk = 20;
    private volatile bool _connected;

    public bool IsOpen => _connected;

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public void Open() => OpenAsync().GetAwaiter().GetResult();

    private async Task OpenAsync()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Bluetooth LE isn't supported by the HANK app on this OS yet - use the USB cable.");
        if (!await IsAvailableAsync())
            throw new IOException("Bluetooth is off or there's no Bluetooth adapter.");

        var device = await FindAsync(deviceName)
            ?? throw new IOException($"No Bluetooth LE device called '{deviceName}' found. Is HANK on and nearby?");
        _device = device;
        device.GattServerDisconnected += OnDisconnected;

        await device.Gatt.ConnectAsync();
        try { await device.Gatt.RequestMtuAsync(247); } catch { /* not every platform lets us ask */ }
        var mtu = device.Gatt.Mtu;
        _chunk = mtu > 23 ? Math.Min(mtu - 3, 244) : 20;

        var service = await device.Gatt.GetPrimaryServiceAsync(BluetoothUuid.FromGuid(ServiceUuid))
            ?? throw new IOException($"'{deviceName}' doesn't offer the HANK (Nordic UART) service. Old firmware?");
        _rx = await service.GetCharacteristicAsync(BluetoothUuid.FromGuid(RxUuid)) ?? throw new IOException("RX characteristic missing.");
        _tx = await service.GetCharacteristicAsync(BluetoothUuid.FromGuid(TxUuid)) ?? throw new IOException("TX characteristic missing.");
        _tx.CharacteristicValueChanged += OnNotify;
        await _tx.StartNotificationsAsync();
        _connected = true;
    }

    /// <summary>Names of nearby devices advertising the HANK service (or named like one).</summary>
    public static async Task<IReadOnlyList<string>> ScanAsync(CancellationToken ct = default)
    {
        if (!IsSupported || !await IsAvailableAsync()) return [];
        var found = await Bluetooth.ScanForDevicesAsync(ScanOptions(), ct);
        return [.. found.Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Order()];
    }

    /// <summary>Bluetooth present and on. Translates "can't reach the OS Bluetooth service" into something actionable.</summary>
    private static async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await Bluetooth.GetAvailabilityAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IOException(OperatingSystem.IsLinux()
                ? "Can't reach the Bluetooth service. Is BlueZ (bluetoothd) installed and running?"
                : $"Can't reach the Bluetooth service: {ex.Message}", ex);
        }
    }

    private static RequestDeviceOptions ScanOptions()
    {
        var options = new RequestDeviceOptions { Timeout = ScanTime };
        var byService = new BluetoothLEScanFilter();
        byService.Services.Add(BluetoothUuid.FromGuid(ServiceUuid));
        options.Filters.Add(byService);
        options.Filters.Add(new BluetoothLEScanFilter { NamePrefix = "HANK" });
        options.Filters.Add(new BluetoothLEScanFilter { NamePrefix = "DeskBuddy" });   // firmware before the rename
        options.OptionalServices.Add(BluetoothUuid.FromGuid(ServiceUuid));
        return options;
    }

    private async Task<BluetoothDevice?> FindAsync(string name)
    {
        using var cts = new CancellationTokenSource(ScanTime + TimeSpan.FromSeconds(2));
        var options = ScanOptions();
        options.Filters.Add(new BluetoothLEScanFilter { Name = name });
        var found = await Bluetooth.ScanForDevicesAsync(options, cts.Token);
        return found.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void OnNotify(object? sender, GattCharacteristicValueChangedEventArgs e)
    {
        if (e.Value is null) return;
        foreach (var line in _splitter.Feed(e.Value)) _lines.Add(line);
    }

    private void OnDisconnected(object? sender, EventArgs e) => _connected = false;

    public string? ReadLine()
    {
        if (_lines.TryTake(out var line, 500)) return line;
        if (!_connected) throw new IOException("Bluetooth connection to HANK was lost.");
        return null;
    }

    public void WriteLine(string line)
    {
        if (!_connected || _rx is null) throw new IOException("Not connected.");
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        for (var off = 0; off < bytes.Length; off += _chunk)
        {
            var chunk = bytes.AsSpan(off, Math.Min(_chunk, bytes.Length - off)).ToArray();
            _rx.WriteValueWithResponseAsync(chunk).GetAwaiter().GetResult();
        }
    }

    public void Dispose()
    {
        _connected = false;
        try
        {
            if (_tx is not null) _tx.CharacteristicValueChanged -= OnNotify;
            if (_device is not null)
            {
                _device.GattServerDisconnected -= OnDisconnected;
                _device.Gatt.Disconnect();
            }
        }
        catch
        {
            // already gone
        }
        _lines.Dispose();
    }
}

/// <summary>Turns BLE notification chunks (which split lines anywhere) back into lines.</summary>
public sealed class LineSplitter(int maxLine = 4096)
{
    private readonly List<byte> _partial = [];

    public IReadOnlyList<string> Feed(ReadOnlySpan<byte> chunk)
    {
        List<string>? lines = null;
        lock (_partial)
        {
            foreach (var b in chunk)
            {
                if (b == (byte)'\n')
                {
                    (lines ??= []).Add(Encoding.UTF8.GetString([.. _partial]).TrimEnd('\r'));
                    _partial.Clear();
                }
                else if (_partial.Count < maxLine)
                {
                    _partial.Add(b);
                }
            }
        }
        return lines ?? [];
    }
}
