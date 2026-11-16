using System.Text.Json.Nodes;
using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Sources;

namespace Dante.Tests;

public sealed class ManualTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public class ProtocolTests
{
    [Fact]
    public void Apps_message_truncates_and_maps_visuals()
    {
        var msg = DeviceProtocol.Apps([new AppDefinition { Id = "id", Name = "A really long application name", Visual = VisualKind.Sparkline, Unit = "units!" }]);
        var app = msg["apps"]![0]!;
        Assert.Equal("apps", msg["t"]!.GetValue<string>());
        Assert.Equal("spark", app["vis"]!.GetValue<string>());
        Assert.Equal(19, app["name"]!.GetValue<string>().Length);
        Assert.Equal("units", app["unit"]!.GetValue<string>());
    }

    [Fact]
    public void Text_is_folded_to_ascii()
    {
        var msg = DeviceProtocol.Say("Café — “quoted” 🎉 done");
        Assert.Equal("Cafe - \"quoted\"  done", msg["txt"]!.GetValue<string>());
    }

    [Fact]
    public void Value_omits_missing_fields()
    {
        var msg = DeviceProtocol.Value("x", null, null, 1, null);
        Assert.Null(msg["v"]);
        Assert.Null(msg["quip"]);
        Assert.Equal(1, msg["lvl"]!.GetValue<int>());
    }

    [Fact]
    public void Cfg_carries_the_single_screen_settings()
    {
        var msg = DeviceProtocol.Cfg(new DeviceSettings { Brightness = 300, Display = DisplayType.Tft35, Flip = true });
        Assert.Equal("""{"t":"cfg","bright":255,"display":"tft","lcdSize":"1602","lcdAddr":39,"flip":true}""", msg.ToJsonString());
        Assert.False(DeviceProtocol.Cfg(new DeviceSettings())["flip"]!.GetValue<bool>());
    }

    [Fact]
    public void Timer_values_carry_seconds_left_total_and_running()
    {
        var msg = DeviceProtocol.Value("pomodoro", 999, "Focus 1/4", 0, null, new TimerReading(1499, 1500, true));
        Assert.Equal("""{"t":"val","id":"pomodoro","lvl":0,"v":1499,"tot":1500,"run":true,"txt":"Focus 1/4"}""", msg.ToJsonString());
        Assert.Equal("timer", DeviceProtocol.VisualWireName(VisualKind.Timer));
    }

    [Fact]
    public void Cfg_selects_the_lcd()
    {
        var msg = DeviceProtocol.Cfg(new DeviceSettings { Display = DisplayType.Lcd1602, LcdAddress = 0x3F });
        Assert.Equal("lcd1602", msg["display"]!.GetValue<string>());
        Assert.Equal(0x3F, msg["lcdAddr"]!.GetValue<int>());
        Assert.Equal(0x77, DeviceProtocol.Cfg(new DeviceSettings { LcdAddress = 500 })["lcdAddr"]!.GetValue<int>());
        Assert.Equal("oled", DeviceProtocol.Cfg(new DeviceSettings { Display = DisplayType.Oled128x32 })["display"]!.GetValue<string>());
        // Default: let DANTE use whatever screen it finds, rather than forcing the OLED.
        Assert.Equal(DisplayType.Auto, new DeviceSettings().Display);
        Assert.Equal("auto", DeviceProtocol.Cfg(new DeviceSettings())["display"]!.GetValue<string>());
    }

    [Fact]
    public void Cfg_selects_the_20x4_lcd()
    {
        var lcd = DeviceProtocol.Cfg(new DeviceSettings { Display = DisplayType.Lcd2004 });
        Assert.Equal("lcd2004", lcd["display"]!.GetValue<string>());
        Assert.Equal("2004", lcd["lcdSize"]!.GetValue<string>());

        // Automatic still tells DANTE which LCD to expect if it finds one.
        var auto = DeviceProtocol.Cfg(new DeviceSettings { Display = DisplayType.Auto, LcdSize = LcdSize.Size20x4 });
        Assert.Equal("auto", auto["display"]!.GetValue<string>());
        Assert.Equal("2004", auto["lcdSize"]!.GetValue<string>());

        // An explicitly picked type wins over the remembered size.
        Assert.Equal("1602", DeviceProtocol.Cfg(new DeviceSettings { Display = DisplayType.Lcd1602, LcdSize = LcdSize.Size20x4 })["lcdSize"]!.GetValue<string>());
    }

    [Fact]
    public void Picking_an_lcd_type_remembers_its_size_for_automatic()
    {
        var cfg = new DanteConfig { Device = { Display = DisplayType.Lcd2004 } };
        ConfigStore.Validate(cfg);
        Assert.Equal(LcdSize.Size20x4, cfg.Device.LcdSize);
        cfg.Device.Display = DisplayType.Auto;
        Assert.Equal(LcdSize.Size20x4, cfg.Device.EffectiveLcdSize);
        var json = System.Text.Json.JsonSerializer.Serialize(cfg, ConfigStore.JsonOptions);
        Assert.DoesNotContain("isLcd", json);
        Assert.DoesNotContain("effectiveLcdSize", json);
        Assert.Contains("\"Size20x4\"", json);
    }

    [Fact]
    public void Parses_device_events_and_ignores_noise()
    {
        var evt = DeviceEvent.TryParse("""{"t":"btn","b":"sel","long":true,"scr":"prs"}""");
        Assert.NotNull(evt);
        Assert.Equal("sel", evt.Button);
        Assert.True(evt.Long);
        Assert.Equal("prs", evt.Screen);
        Assert.Null(DeviceEvent.TryParse("ets Jun  8 2016 00:22:57 rst:0x1 (POWERON_RESET)"));
    }
}

public class ClaudeCodeTrackerTests
{
    private static JsonObject Hook(string evt, string session = "s1", string cwd = "/home/me/repos/DANTE") =>
        new() { ["hook_event_name"] = evt, ["session_id"] = session, ["cwd"] = cwd, ["message"] = "Claude needs your permission" };

    [Fact]
    public void Notification_then_prompt_clears()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var tracker = new ClaudeCodeSessionTracker(time);
        tracker.Handle(Hook("Notification"));
        var w = Assert.Single(tracker.Waiting);
        Assert.Equal("DANTE", w.Project);

        tracker.Handle(Hook("UserPromptSubmit"));
        Assert.Empty(tracker.Waiting);
    }

    [Fact]
    public void Waiting_since_is_kept_across_events()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var tracker = new ClaudeCodeSessionTracker(time);
        tracker.Handle(Hook("Stop"));
        time.Now = time.Now.AddMinutes(10);
        tracker.Handle(Hook("Notification"));
        Assert.Equal(DateTimeOffset.UnixEpoch, Assert.Single(tracker.Waiting).Since);
    }

    [Fact]
    public async Task Source_can_ignore_finished_sessions()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var tracker = new ClaudeCodeSessionTracker(time);
        tracker.Handle(Hook("Stop", "a"));
        tracker.Handle(Hook("Notification", "b", "/x/other"));
        var source = new ClaudeCodeWaitingSource(tracker, time);

        var all = await source.ReadAsync(new AppDefinition(), default);
        Assert.Equal(2, all.Value);

        var strict = await source.ReadAsync(new AppDefinition { SourceSettings = new() { ["includeFinished"] = "false" } }, default);
        Assert.Equal(1, strict.Value);
        Assert.Equal("other", strict.Pending![0].Title);
    }
}

public class AzureDevOpsTests
{
    private const string Me = "11111111-aaaa-bbbb-cccc-000000000001";

    private static JsonObject Pr(int id, string creator, int myVote, bool draft = false, int otherVote = 0) => new()
    {
        ["pullRequestId"] = id,
        ["title"] = $"PR {id}",
        ["isDraft"] = draft,
        ["creationDate"] = "2026-11-10T08:00:00Z",
        ["createdBy"] = new JsonObject { ["id"] = creator },
        ["repository"] = new JsonObject { ["name"] = "repo", ["project"] = new JsonObject { ["name"] = "Proj" } },
        ["reviewers"] = new JsonArray(
            new JsonObject { ["id"] = Me, ["vote"] = myVote },
            new JsonObject { ["id"] = "someone-else", ["vote"] = otherVote }),
    };

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Review_mode_counts_unvoted_prs_by_others()
    {
        var result = AzureDevOpsPullRequestSource.Evaluate(
            [Pr(1, "other", 0), Pr(2, "other", 10), Pr(3, "other", 0, draft: true), Pr(1, "other", 0)],
            Me, "review", includeDrafts: false, "https://dev.azure.com/org", Now);
        Assert.Equal(1, result.Value);
        var item = Assert.Single(result.Pending!);
        Assert.Equal("https://dev.azure.com/org/Proj/_git/repo/pullrequest/1", item.Url);
        Assert.Equal("oldest 4h", result.Text);
    }

    [Fact]
    public void Mine_mode_counts_my_prs_waiting_for_me()
    {
        var result = AzureDevOpsPullRequestSource.Evaluate(
            [Pr(5, Me, 0, otherVote: -5), Pr(6, Me, 0, otherVote: 10)],
            Me, "mine", includeDrafts: false, "https://dev.azure.com/org", Now);
        Assert.Equal(1, result.Value);
    }
}

public class GenericSourceTests
{
    [Theory]
    [InlineData("42", 42.0, "")]
    [InlineData("3.5 builds broken", 3.5, "builds broken")]
    [InlineData("""{"value": 7, "text": "seven"}""", 7.0, "seven")]
    public void Command_output_parsing(string stdout, double value, string text)
    {
        var r = CommandSource.ParseOutput(stdout);
        Assert.Equal(value, r.Value);
        Assert.Equal(text, r.Text);
    }

    [Fact]
    public void Json_paths()
    {
        var json = JsonNode.Parse("""{"data":{"items":[{"n":"4"},{"n":5}],"title":"hi"}}""");
        Assert.Equal(4, JsonPath.Number(json, "data.items[0].n"));
        Assert.Equal(5, JsonPath.Number(json, "data.items[1].n"));
        Assert.Equal(2, JsonPath.Number(json, "$count:data.items"));
        Assert.Equal("hi", JsonPath.Text(json, "data.title"));
        Assert.Null(JsonPath.Number(json, "data.nope[3]"));
    }

    [Fact]
    public void Claude_usage_parsing()
    {
        var now = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
        var json = JsonNode.Parse("""{"five_hour":{"utilization":63.25,"resets_at":"2026-11-11T12:10:00Z"},"seven_day":{"utilization":20}}""") as JsonObject;
        var r = ClaudeCodeUsageSource.Parse(json, "five_hour", now);
        Assert.Equal(63.3, r.Value);
        Assert.Equal("resets in 2h10m", r.Text);
        Assert.NotNull(ClaudeCodeUsageSource.Parse(json, "seven_day_opus", now).Error);
    }

    [Fact]
    public void Env_secrets_resolve()
    {
        Environment.SetEnvironmentVariable("DANTE_TEST_SECRET", "s3cret");
        Assert.Equal("s3cret", SourceSettingsExtensions.ResolveSecret("env:DANTE_TEST_SECRET"));
        Assert.Equal("plain", SourceSettingsExtensions.ResolveSecret("plain"));
    }
}

public class ConfigTests
{
    [Fact]
    public void Validate_makes_unique_slugs()
    {
        var cfg = new DanteConfig { Apps = [new() { Name = "My PRs!" }, new() { Name = "my prs" }, new() { Id = "x", Name = "X", Max = 0, Min = 5 }] };
        ConfigStore.Validate(cfg);
        Assert.Equal("my_prs", cfg.Apps[0].Id);
        Assert.Equal("my_prs_2", cfg.Apps[1].Id);
        Assert.True(cfg.Apps[2].Max > cfg.Apps[2].Min);
    }

    [Fact]
    public void Save_and_reload_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}", "config.json");
        var store = new ConfigStore(path);
        var cfg = store.Snapshot();
        cfg.Personality = Personality.Dramatic;
        cfg.Apps[0].Takeover.Mode = TakeoverMode.WhenAbove;
        cfg.Device.Flip = true;
        cfg.Device.Display = DisplayType.Lcd1602;
        cfg.Device.LcdAddress = 0x3F;
        cfg.Device.Connection = ConnectionKind.Usb;
        cfg.Device.BluetoothName = "Buddy2";
        store.Save(cfg);

        var reloaded = new ConfigStore(path).Snapshot();
        Assert.Equal(Personality.Dramatic, reloaded.Personality);
        Assert.Equal(TakeoverMode.WhenAbove, reloaded.Apps[0].Takeover.Mode);
        Assert.True(reloaded.Device.Flip);
        Assert.Equal(DisplayType.Lcd1602, reloaded.Device.Display);
        Assert.Equal(0x3F, reloaded.Device.LcdAddress);
        Assert.Equal(ConnectionKind.Usb, reloaded.Device.Connection);
        Assert.Equal("Buddy2", reloaded.Device.BluetoothName);
        Assert.Contains("\"Lcd1602\"", File.ReadAllText(path));
        Assert.Contains("\"WhenAbove\"", File.ReadAllText(path));
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
}

public class HookInstallerTests
{
    [Fact]
    public void Install_is_idempotent_and_preserves_existing_settings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, """
            {
              // user comment
              "model": "opus",
              "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ] }
            }
            """);

        var installer = new ClaudeHookInstaller(path, "http://127.0.0.1:5280");
        Assert.False(installer.IsInstalled());
        installer.Install();
        installer.Install();
        Assert.True(installer.IsInstalled());

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("opus", json["model"]!.GetValue<string>());
        Assert.Equal(2, json["hooks"]!["Stop"]!.AsArray().Count);
        Assert.Equal("*", json["hooks"]!["PostToolUse"]![0]!["matcher"]!.GetValue<string>());

        installer.Uninstall();
        json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Single(json["hooks"]!["Stop"]!.AsArray());
        Assert.Null(json["hooks"]!["Notification"]);
        Directory.Delete(dir, recursive: true);
    }
}
