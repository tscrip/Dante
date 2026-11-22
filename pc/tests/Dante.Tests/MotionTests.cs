using Dante.Core.Config;
using Dante.Core.Device;

namespace Dante.Tests;

/// <summary>The "Movement" setting (how lively the face is) and how it reaches DANTE.</summary>
public class MotionTests
{
    [Theory]
    [InlineData(Motion.Auto, "auto")]
    [InlineData(Motion.Lively, "lively")]
    [InlineData(Motion.Calm, "calm")]
    public void The_setting_goes_to_DANTE_in_cfg(Motion motion, string wire) =>
        Assert.Equal(wire, DeviceProtocol.Cfg(new DeviceSettings { Motion = motion })["motion"]!.GetValue<string>());

    [Fact]
    public void Old_configs_without_it_are_automatic_and_nonsense_falls_back_to_automatic()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, """{ "device": { "look": "Nerd" } }""");
            Assert.Equal(Motion.Auto, new ConfigStore(path).Snapshot().Device.Motion);

            File.WriteAllText(path, """{ "device": { "motion": 7 } }""");
            Assert.Equal(Motion.Auto, new ConfigStore(path).Snapshot().Device.Motion);

            var store = new ConfigStore(path);
            var cfg = store.Snapshot();
            cfg.Device.Motion = Motion.Calm;
            store.Save(cfg);
            Assert.Equal(Motion.Calm, new ConfigStore(path).Snapshot().Device.Motion);
            Assert.Contains("\"Calm\"", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
