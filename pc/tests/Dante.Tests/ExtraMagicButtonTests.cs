using Dante.Core.Config;
using Dante.Core.Device;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Tests;

/// <summary>The yellow board's extra MAGIC button (IO22). There is nothing to switch on: it just works when wired.</summary>
public class ExtraMagicButtonTests
{
    [Fact]
    public void Old_configs_from_the_light_version_still_load_and_cfg_no_longer_sends_those_settings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Firmware 3.10 had a notification light and an on/off switch for the button: both are gone.
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, """{ "device": { "look": "Nerd", "light": "Nudges", "extraMagicButton": true } }""");
            var device = new ConfigStore(path).Snapshot().Device;
            Assert.Equal(Look.Nerd, device.Look);

            var cfg = DeviceProtocol.Cfg(device);
            Assert.Null(cfg["light"]);
            Assert.Null(cfg["xbtn"]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task DANTE_says_in_its_hello_whether_it_can_have_the_button()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        var store = new ConfigStore(Path.Combine(dir, "config.json"));
        var cfg = store.Snapshot();
        cfg.Device.PortName = "FAKE";
        store.Save(cfg);
        var dante = new FakeDante();
        var link = new DeviceLink(store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => dante);
        await link.StartAsync(default);
        try
        {
            for (var i = 0; i < 60 && link.Status != LinkStatus.Connected; i++) await Task.Delay(50);
            for (var i = 0; i < 40 && link.Firmware is null; i++) await Task.Delay(50);
            Assert.False(link.HasExtraButton);   // FakeDante answers like a board with buttons

            dante.Press("""{"t":"hello","fw":"3.11.0","name":"DANTE","display":"tft","oled":false,"touch":true,"xbtn":true}""");
            for (var i = 0; i < 40 && !link.HasExtraButton; i++) await Task.Delay(50);
            Assert.True(link.HasExtraButton);
        }
        finally
        {
            await link.StopAsync(default);
            Directory.Delete(dir, recursive: true);
        }
    }
}
