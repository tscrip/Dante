using Dante.Core.Config;
using Dante.Core.Device;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Tests;

public class SimulatorTests
{
    [Theory]
    [InlineData(SimulatedHardware.Tft, "tft")]
    [InlineData(SimulatedHardware.Oled, "oled")]
    [InlineData(SimulatedHardware.Lcd1602, "lcd")]
    [InlineData(SimulatedHardware.Lcd2004, "lcd")]
    public void Hardware_maps_to_the_simulator_argument(SimulatedHardware hw, string arg) =>
        Assert.Equal(arg, hw.Argument());

    [Fact]
    public void Picking_hardware_points_the_screen_settings_at_it()
    {
        var d = new DeviceSettings { Display = DisplayType.Oled128x32, LcdSize = LcdSize.Size16x2 };
        SimulatedHardware.Lcd2004.ApplyTo(d);
        Assert.Equal(SimulatedHardware.Lcd2004, d.SimulatedHardware);
        Assert.Equal(DisplayType.Auto, d.Display);        // the firmware's own detection decides
        Assert.Equal(LcdSize.Size20x4, d.LcdSize);        // the one thing an LCD can't report itself
    }

    [Fact]
    public void Simulator_page_sits_just_above_device_in_the_sidebar()
    {
        var nav = Dante.Server.Features.FeatureCatalog.Discover(typeof(Dante.Server.Features.IFeature).Assembly).Navigation;
        Assert.Equal("simulator", nav[^2].Href);
        Assert.Equal("device", nav[^1].Href);
    }

    [Fact]
    public void Locator_finds_the_built_program_by_walking_up()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        var bin = Path.Combine(root, "firmware", ".pio", "build", "simulator", OperatingSystem.IsWindows() ? "program.exe" : "program");
        var deep = Path.Combine(root, "pc", "src", "Dante.Server", "bin");
        Directory.CreateDirectory(Path.GetDirectoryName(bin)!);
        Directory.CreateDirectory(deep);
        File.WriteAllText(bin, "");
        try
        {
            Assert.Equal(bin, SimulatorLocator.Find(null, [deep]));
            var missing = Assert.Throws<FileNotFoundException>(() => SimulatorLocator.Find(null, [Path.GetTempPath()]));
            Assert.Contains("pio run -e simulator", missing.Message);
            Assert.Throws<FileNotFoundException>(() => SimulatorLocator.Find(Path.Combine(root, "nope")));
            Assert.Equal(bin, SimulatorLocator.Find(bin));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Frames_are_kept_for_the_page_not_logged_and_buttons_reach_the_simulator()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        var store = new ConfigStore(Path.Combine(dir, "config.json"));
        var cfg = store.Snapshot();
        cfg.Device.Connection = ConnectionKind.Simulator;
        store.Save(cfg);

        var sim = new FakeDante();
        var link = new DeviceLink(store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => sim);
        string? seen = null;
        link.SimFrameReceived += f => seen = f;
        await link.StartAsync(default);
        try
        {
            for (var i = 0; i < 60 && link.Status != LinkStatus.Connected; i++) await Task.Delay(50);
            Assert.Equal("Simulator (3.5\" TFT)", link.PortName);

            const string frame = """{"t":"sim.frame","fw":"t","kind":"tft","w":480,"h":320,"light":200,"rle":"AQAAAA=="}""";
            sim.Press(frame);
            for (var i = 0; i < 40 && seen is null; i++) await Task.Delay(50);
            Assert.Equal(frame, seen);
            Assert.Equal(frame, link.LastSimFrame);
            Assert.DoesNotContain(link.RecentLog(), l => l.Text.Contains("sim.frame"));

            link.SimButton("magic", down: true);
            for (var i = 0; i < 40 && !sim.Received.Any(m => m["t"]!.GetValue<string>() == "sim.btn"); i++) await Task.Delay(50);
            var btn = Assert.Single(sim.Received, m => m["t"]!.GetValue<string>() == "sim.btn");
            Assert.Equal("magic", btn["b"]!.GetValue<string>());
            Assert.True(btn["down"]!.GetValue<bool>());
        }
        finally
        {
            await link.StopAsync(default);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_connect_never_leaves_a_disposed_session_behind()
    {
        // Regression: when opening the link threw (e.g. Bluetooth off), Reconnect() later hit a disposed
        // CancellationTokenSource and crashed whatever page changed the settings.
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        var store = new ConfigStore(Path.Combine(dir, "config.json"));
        var attempts = 0;
        var link = new DeviceLink(store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ =>
        {
            Interlocked.Increment(ref attempts);
            throw new IOException("no Bluetooth here");
        });
        await link.StartAsync(default);
        try
        {
            for (var i = 0; i < 40 && attempts == 0; i++) await Task.Delay(50);
            await Task.Delay(100);
            link.Reconnect();                                   // must not throw
            var cfg = store.Snapshot();
            cfg.Device.Connection = ConnectionKind.Usb;
            cfg.Device.PortName = "COM9";
            store.Save(cfg);                                    // must not throw either...
            for (var i = 0; i < 40 && attempts < 2; i++) await Task.Delay(50);
            Assert.True(attempts >= 2, "...and a settings change retries straight away instead of after the 3 s wait");
            Assert.Equal("no Bluetooth here", link.LastError);
        }
        finally
        {
            await link.StopAsync(default);
            Directory.Delete(dir, recursive: true);
        }
    }
}
