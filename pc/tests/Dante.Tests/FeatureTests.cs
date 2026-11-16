using Dante.Core.Config;
using Dante.Server.Features;
using Dante.Server.Features.Calendar;
using Dante.Server.Features.Pomodoro;

namespace Dante.Tests;

public class FeatureCatalogTests
{
    [Fact]
    public void Finds_every_slice_and_builds_the_sidebar_from_them()
    {
        var catalog = FeatureCatalog.Discover(typeof(IFeature).Assembly);
        var names = catalog.Features.Select(f => f.GetType().Name).ToList();
        Assert.Contains("PomodoroFeature", names);
        Assert.Contains("CalendarFeature", names);
        Assert.Contains("MagicButtonFeature", names);
        Assert.Contains("PushFeature", names);
        Assert.Equal("Dashboard", catalog.Navigation[0].Title);   // order 0 first
        Assert.Equal("Device", catalog.Navigation[^1].Title);     // order 90 last
        Assert.Contains(catalog.Navigation, n => n is { Title: "Pomodoro", Href: "pomodoro" });
        Assert.Contains(catalog.Navigation, n => n is { Title: "Calendar", Href: "calendar" });
    }
}

public class FeatureSettingsTests
{
    [Fact]
    public void Older_configs_are_carried_over()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, """
        {
          "device": { "display": "Oled128x32", "screenCount": 2, "dualLayout": "Mirror", "flipScreen1": true, "flipScreen2": false },
          "magic": { "press": { "kind": "NextTrack" }, "longPress": { "kind": "RefreshApps" } },
          "apps": []
        }
        """);

        var cfg = new ConfigStore(path).Snapshot();
        Assert.True(cfg.Device.Flip);
        var magic = cfg.Feature<Dante.Server.Features.MagicButton.MagicButtonSettings>("magic");
        Assert.Equal(Dante.Server.Features.MagicButton.MagicActionKind.NextTrack, magic.Press.Kind);
        Assert.Equal(Dante.Server.Features.MagicButton.MagicActionKind.RefreshApps, magic.LongPress.Kind);

        new ConfigStore(path).Save(cfg);
        var saved = File.ReadAllText(path);
        Assert.DoesNotContain("flipScreen1", saved);
        Assert.DoesNotContain("screenCount", saved);
        Assert.DoesNotContain("\"magic\": {\n    \"press\"", saved.Replace("\r", ""));   // only under "features" now
        Assert.Contains("\"features\"", saved);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Feature_settings_round_trip_and_default_when_missing()
    {
        var cfg = new DanteConfig();
        Assert.Equal(25, cfg.Feature<PomodoroSettings>(PomodoroSettings.Key).FocusMinutes);
        cfg.SetFeature(PomodoroSettings.Key, new PomodoroSettings { FocusMinutes = 50 });
        cfg.SetFeature(CalendarSettings.Key, new CalendarSettings { Enabled = true, ReminderMinutes = [5] });

        var copy = ConfigStore.Clone(cfg);
        Assert.Equal(50, copy.Feature<PomodoroSettings>(PomodoroSettings.Key).FocusMinutes);
        Assert.Equal([5], copy.Feature<CalendarSettings>(CalendarSettings.Key).ReminderMinutes);
    }
}
