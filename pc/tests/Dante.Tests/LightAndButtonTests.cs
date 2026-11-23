using Dante.Core.Config;
using Dante.Core.Device;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Tests;

/// <summary>The yellow board's P3 extras: a notification light and an extra MAGIC button.</summary>
public class LightAndButtonTests
{
    [Theory]
    [InlineData(NotificationLight.Waiting, "waiting")]
    [InlineData(NotificationLight.Nudges, "nudges")]
    [InlineData(NotificationLight.Off, "off")]
    public void The_light_setting_goes_to_DANTE_in_cfg(NotificationLight light, string wire) =>
        Assert.Equal(wire, DeviceProtocol.Cfg(new DeviceSettings { Light = light })["light"]!.GetValue<string>());

    [Fact]
    public void The_extra_button_is_off_unless_switched_on()
    {
        Assert.False(DeviceProtocol.Cfg(new DeviceSettings())["xbtn"]!.GetValue<bool>());
        Assert.True(DeviceProtocol.Cfg(new DeviceSettings { ExtraMagicButton = true })["xbtn"]!.GetValue<bool>());
    }

    [Fact]
    public void Old_configs_load_with_the_light_on_for_waiting_things_and_no_extra_button()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, """{ "device": { "look": "Party" } }""");
            var device = new ConfigStore(path).Snapshot().Device;
            Assert.Equal(NotificationLight.Waiting, device.Light);
            Assert.False(device.ExtraMagicButton);

            File.WriteAllText(path, """{ "device": { "light": 9 } }""");   // nonsense: back to the default
            Assert.Equal(NotificationLight.Waiting, new ConfigStore(path).Snapshot().Device.Light);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task DANTE_says_in_its_hello_whether_it_has_the_light_and_the_button_connector()
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
            // FakeDante answers the PC's hello like a board without extras.
            for (var i = 0; i < 40 && link.Firmware is null; i++) await Task.Delay(50);
            Assert.False(link.HasLight);
            Assert.False(link.HasExtraButton);

            dante.Press("""{"t":"hello","fw":"3.10.0","name":"DANTE","display":"tft","oled":false,"touch":true,"light":true,"xbtn":true}""");
            for (var i = 0; i < 40 && !link.HasExtraButton; i++) await Task.Delay(50);
            Assert.True(link.HasLight);
            Assert.True(link.HasExtraButton);
        }
        finally
        {
            await link.StopAsync(default);
            Directory.Delete(dir, recursive: true);
        }
    }
}
