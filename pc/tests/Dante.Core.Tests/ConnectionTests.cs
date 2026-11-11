using System.Text;
using Dante.Core.Config;
using Dante.Core.Device;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Core.Tests;

public class ConnectionTests
{
    [Fact]
    public void Ble_chunks_are_reassembled_into_lines()
    {
        var splitter = new LineSplitter();
        Assert.Empty(splitter.Feed(Encoding.UTF8.GetBytes("{\"t\":\"hel")));
        var lines = splitter.Feed(Encoding.UTF8.GetBytes("lo\"}\r\n{\"t\":\"pong\"}\n{\"t\":"));
        Assert.Equal(["{\"t\":\"hello\"}", "{\"t\":\"pong\"}"], lines);
        Assert.Equal(["{\"t\":\"ack\"}"], splitter.Feed(Encoding.UTF8.GetBytes("\"ack\"}\n")));
    }

    [Fact]
    public void Oversize_lines_are_cut_not_unbounded()
    {
        var splitter = new LineSplitter(maxLine: 8);
        var line = Assert.Single(splitter.Feed(Encoding.UTF8.GetBytes(new string('x', 100) + "\n")));
        Assert.Equal(8, line.Length);
    }

    [Fact]
    public void Defaults_to_bluetooth_le_named_dante()
    {
        var d = new DeviceSettings();
        Assert.Equal(ConnectionKind.Bluetooth, d.Connection);
        Assert.Equal("DANTE", d.BluetoothName);
    }

    [Fact]
    public async Task Switching_connection_reconnects_with_the_new_settings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        var store = new ConfigStore(Path.Combine(dir, "config.json"));
        var cfg = store.Snapshot();
        cfg.Device.Connection = ConnectionKind.Bluetooth;
        cfg.Device.BluetoothName = "DANTE";
        store.Save(cfg);

        var opened = new List<(ConnectionKind Kind, string? Target)>();
        var link = new DeviceLink(store, NullLogger<DeviceLink>.Instance, TimeProvider.System, s =>
        {
            lock (opened) opened.Add((s.Connection, s.Connection == ConnectionKind.Usb ? s.PortName : s.BluetoothName));
            return new FakeDante();
        });
        await link.StartAsync(default);
        try
        {
            for (var i = 0; i < 60 && link.Status != LinkStatus.Connected; i++) await Task.Delay(50);
            Assert.Equal("Bluetooth: DANTE", link.PortName);

            cfg.Device.Brightness = 10;          // not a connection change: keep the link
            store.Save(cfg);
            await Task.Delay(300);
            lock (opened) Assert.Single(opened);

            cfg.Device.Connection = ConnectionKind.Usb;
            cfg.Device.PortName = "COM7";
            store.Save(cfg);
            for (var i = 0; i < 100 && link.PortName != "COM7"; i++) await Task.Delay(50);
            Assert.Equal("COM7", link.PortName);
            lock (opened) Assert.Equal([(ConnectionKind.Bluetooth, "DANTE"), (ConnectionKind.Usb, "COM7")], opened);
        }
        finally
        {
            await link.StopAsync(default);
            Directory.Delete(dir, recursive: true);
        }
    }
}
