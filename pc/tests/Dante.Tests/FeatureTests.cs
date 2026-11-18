using Dante.Core.Config;
using Dante.Core.Plugins;
using Dante.Core.Widgets;
using Dante.Server.Features;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Contains(catalog.Navigation, n => n is { Title: "Widgets", Href: "widgets" });
        Assert.Contains(catalog.Navigation, n => n is { Title: "Plugins", Href: "plugins" });
        // Plugins are listed under "Plugins" from the plugin registry, not as their own sidebar entries.
        Assert.DoesNotContain(catalog.Navigation, n => n.Href is "pomodoro" or "calendar" or "claude-code");
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

public class PluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services;

    public PluginTests()
    {
        // The same kernel + feature registrations as Program.cs.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new ConfigStore(Path.Combine(_dir, "config.json")));
        services.AddSingleton<Dante.Core.Engine.IUrlOpener, RecordingOpener>();
        services.AddSingleton(sp => new Dante.Core.Device.DeviceLink(sp.GetRequiredService<ConfigStore>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Dante.Core.Device.DeviceLink>.Instance, TimeProvider.System));
        services.AddSingleton<Dante.Core.Device.DeviceAlerts>();
        services.AddSingleton<WidgetTypeRegistry>();
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<PluginSettings>();
        services.AddSingleton<Dante.Core.Engine.DanteEngine>();
        services.AddFeatures();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Every_widget_type_belongs_to_a_plugin_and_Claude_offers_two()
    {
        var plugins = _services.GetRequiredService<PluginRegistry>();
        var types = _services.GetRequiredService<WidgetTypeRegistry>();
        Assert.All(types.All, t => Assert.NotNull(plugins.Find(t.PluginId)));
        Assert.Equal(["Sessions waiting on me", "Subscription usage"], plugins.WidgetTypes("claude-code").Select(t => t.DisplayName));
        Assert.Equal(["claude-code", "azure-devops", "calendar", "pomodoro", "days-until", "web-api", "command", "push"], plugins.All.Select(p => p.Id));
        Assert.Contains(plugins.All, p => p.ConfigComponent is not null);   // Claude, Calendar, Pomodoro bring their own panels
    }

    [Fact]
    public void Default_widgets_match_their_types()
    {
        var types = _services.GetRequiredService<WidgetTypeRegistry>();
        foreach (var w in DefaultConfig.Create().Widgets) Assert.NotNull(types.Find(w.Type));
        var usage = types.Find("claude-code-usage")!.CreateDefault();
        Assert.Equal((VisualKind.Gauge, "%", 90.0), (usage.Visual, usage.Unit, usage.Levels.CritAt!.Value));
        Assert.False(types.Find("pomodoro")!.AllowMultiple);
    }

    [Fact]
    public void A_widget_value_wins_over_the_plugin_value_which_wins_over_the_default()
    {
        var settings = _services.GetRequiredService<PluginSettings>();
        var ado = _services.GetRequiredService<PluginRegistry>().Find("azure-devops")!;
        var widget = new WidgetDefinition { Type = "azure-devops-prs" };

        Assert.Equal("https://dev.azure.com/your-org", settings.Get(ado, "organization", widget));
        settings.Save("azure-devops", new() { ["organization"] = "https://dev.azure.com/contoso" });
        Assert.Equal("https://dev.azure.com/contoso", settings.Get(ado, "organization", widget));
        widget.Settings["organization"] = "https://dev.azure.com/other";
        Assert.Equal("https://dev.azure.com/other", settings.Get(ado, "organization", widget));

        Environment.SetEnvironmentVariable("DANTE_TEST_PAT", "secret");
        settings.Save("azure-devops", new() { ["pat"] = "env:DANTE_TEST_PAT" });
        Assert.Equal("secret", settings.Get(ado, "pat"));
        Environment.SetEnvironmentVariable("DANTE_TEST_PAT", null);
    }

    [Fact]
    public void Shared_settings_kept_on_old_widgets_move_to_the_plugin_once()
    {
        var store = _services.GetRequiredService<ConfigStore>();
        var cfg = store.Snapshot();
        cfg.Widgets.Clear();
        cfg.Widgets.Add(new WidgetDefinition { Id = "a", Type = "azure-devops-prs", Settings = new() { ["organization"] = "https://dev.azure.com/contoso", ["pat"] = "env:X", ["mode"] = "mine" } });
        cfg.Widgets.Add(new WidgetDefinition { Id = "b", Type = "azure-devops-prs", Settings = new() { ["organization"] = "https://dev.azure.com/elsewhere" } });
        store.Save(cfg);

        PluginMigration.MoveToPlugin(store, "azure-devops", "azure-devops-prs", "organization", "pat");
        var moved = _services.GetRequiredService<PluginSettings>().Load("azure-devops");
        Assert.Equal("https://dev.azure.com/contoso", moved["organization"]);
        Assert.Equal("env:X", moved["pat"]);
        var after = store.Snapshot().Widgets;
        Assert.Equal(["mode"], after[0].Settings.Keys);                                        // moved off the widget
        Assert.Equal("https://dev.azure.com/elsewhere", after[1].Settings["organization"]);   // a different org stays as an override

        // Once the plugin has settings it never runs again.
        cfg = store.Snapshot(); cfg.Widgets[0].Settings["organization"] = "https://dev.azure.com/new"; store.Save(cfg);
        PluginMigration.MoveToPlugin(store, "azure-devops", "azure-devops-prs", "organization", "pat");
        Assert.Equal("https://dev.azure.com/new", store.Snapshot().Widgets[0].Settings["organization"]);
    }
}

public class WidgetRenameMigrationTests
{
    [Fact]
    public void Apps_become_widgets()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, """
        {
          "apps": [
            { "id": "prs", "name": "PRs", "enabled": false, "sourceType": "azure-devops-prs", "sourceSettings": { "mode": "mine" } },
            { "id": "cc", "name": "Claude", "sourceType": "claude-code-waiting" }
          ]
        }
        """);
        var cfg = new ConfigStore(path).Snapshot();
        Assert.Equal(["prs", "cc"], cfg.Widgets.Select(w => w.Id));
        Assert.False(cfg.Widgets[0].Active);
        Assert.True(cfg.Widgets[1].Active);   // missing = active, like "enabled" was
        Assert.Equal("azure-devops-prs", cfg.Widgets[0].Type);
        Assert.Equal("mine", cfg.Widgets[0].Settings["mode"]);

        new ConfigStore(path).Save(cfg);
        var saved = File.ReadAllText(path);
        Assert.Contains("\"widgets\"", saved);
        Assert.DoesNotContain("\"apps\"", saved);
        Assert.DoesNotContain("sourceType", saved);
        Directory.Delete(dir, recursive: true);
    }
}
