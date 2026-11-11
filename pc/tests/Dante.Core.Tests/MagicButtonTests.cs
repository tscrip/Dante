using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Dante.Core.Actions;
using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;
using Dante.Core.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Core.Tests;

/// <summary>Records commands; answers with canned results ("program arg arg" -> result).</summary>
public sealed class FakeRunner : ICommandRunner
{
    public List<string> Calls { get; } = [];
    public Dictionary<string, CommandResult> Results { get; } = [];
    public HashSet<string> Missing { get; } = [];

    public Task<CommandResult> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        var line = string.Join(' ', [file, .. args]);
        Calls.Add(line);
        if (Missing.Contains(file)) return Task.FromResult(new CommandResult(-1, "", "not found", NotFound: true));
        return Task.FromResult(Results.TryGetValue(line, out var r) ? r : new CommandResult(0, "", ""));
    }
}

public class LinuxBackendTests
{
    [Fact]
    public async Task Pipewire_mic_mute_reports_state()
    {
        var run = new FakeRunner();
        run.Results["wpctl get-volume @DEFAULT_AUDIO_SOURCE@"] = new(0, "Volume: 0.80 [MUTED]", "");
        var muted = await new LinuxMediaBackend(run).ToggleMicMuteAsync(default);
        Assert.True(muted);
        Assert.Equal(["wpctl set-mute @DEFAULT_AUDIO_SOURCE@ toggle", "wpctl get-volume @DEFAULT_AUDIO_SOURCE@"], run.Calls);
    }

    [Fact]
    public async Task Falls_back_to_pulseaudio_when_wpctl_is_missing()
    {
        var run = new FakeRunner { Missing = { "wpctl" } };
        run.Results["pactl get-sink-mute @DEFAULT_SINK@"] = new(0, "Mute: no", "");
        Assert.False(await new LinuxMediaBackend(run).ToggleSpeakerMuteAsync(default));
        Assert.Contains("pactl set-sink-mute @DEFAULT_SINK@ toggle", run.Calls);
    }

    [Fact]
    public async Task Volume_is_read_back_in_percent()
    {
        var run = new FakeRunner();
        run.Results["wpctl get-volume @DEFAULT_AUDIO_SINK@"] = new(0, "Volume: 0.45", "");
        Assert.Equal(45, await new LinuxMediaBackend(run).ChangeVolumeAsync(up: true, default));
        Assert.Equal("wpctl set-volume -l 1.0 @DEFAULT_AUDIO_SINK@ 5%+", run.Calls[0]);

        var pulse = new FakeRunner { Missing = { "wpctl" } };
        pulse.Results["pactl get-sink-volume @DEFAULT_SINK@"] = new(0, "Volume: front-left: 26214 /  40% / -23.88 dB,   front-right: 26214 /  40% / -23.88 dB", "");
        Assert.Equal(40, await new LinuxMediaBackend(pulse).ChangeVolumeAsync(up: false, default));
        Assert.Contains("pactl set-sink-volume @DEFAULT_SINK@ -5%", pulse.Calls);
    }

    [Fact]
    public async Task Missing_tools_give_a_helpful_message()
    {
        var noPlayer = new FakeRunner { Missing = { "playerctl" } };
        var ex = await Assert.ThrowsAsync<ActionFailedException>(() => new LinuxMediaBackend(noPlayer).PlayPauseAsync(default));
        Assert.Contains("playerctl", ex.Message);

        var noAudio = new FakeRunner { Missing = { "wpctl", "pactl" } };
        ex = await Assert.ThrowsAsync<ActionFailedException>(() => new LinuxMediaBackend(noAudio).ToggleMicMuteAsync(default));
        Assert.Contains("wpctl", ex.Message);
    }
}

public class MacBackendTests
{
    [Fact]
    public async Task Mic_mute_restores_the_previous_input_level()
    {
        var run = new FakeRunner();
        var mac = new MacMediaBackend(run);
        run.Results["osascript -e return input volume of (get volume settings)"] = new(0, "40", "");
        Assert.True(await mac.ToggleMicMuteAsync(default));
        Assert.Contains("osascript -e set volume input volume 0", run.Calls);

        run.Results["osascript -e return input volume of (get volume settings)"] = new(0, "0", "");
        Assert.False(await mac.ToggleMicMuteAsync(default));
        Assert.Contains("osascript -e set volume input volume 40", run.Calls);
    }

    [Fact]
    public async Task Media_keys_post_system_defined_events()
    {
        var run = new FakeRunner();
        await new MacMediaBackend(run).NextAsync(default);
        var call = Assert.Single(run.Calls);
        Assert.StartsWith("osascript -l JavaScript -e ObjC.import('Cocoa');", call);
        Assert.Contains("(17 << 16)", call);   // NX_KEYTYPE_NEXT
    }
}

public class PcActionsTests
{
    private sealed class NullMedia : IMediaBackend
    {
        public Task PlayPauseAsync(CancellationToken ct) => Task.CompletedTask;
        public Task NextAsync(CancellationToken ct) => Task.CompletedTask;
        public Task PreviousAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ToggleSpeakerMuteAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> ToggleMicMuteAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<int?> ChangeVolumeAsync(bool up, CancellationToken ct) => Task.FromResult<int?>(up ? 55 : null);
    }

    private static PcActions Make(FakeRunner? run = null, RecordingOpener? urls = null) =>
        new(new NullMedia(), run ?? new FakeRunner(), urls ?? new RecordingOpener());

    [Theory]
    [InlineData(MagicActionKind.ToggleSpeakerMute, "Sound muted")]
    [InlineData(MagicActionKind.ToggleMicMute, "Mic is live!")]
    [InlineData(MagicActionKind.VolumeUp, "Volume 55%")]
    [InlineData(MagicActionKind.VolumeDown, "Volume down")]
    [InlineData(MagicActionKind.PlayPause, "Play / pause")]
    public async Task Messages_for_the_buddy(MagicActionKind kind, string expected) =>
        Assert.Equal(expected, await Make().RunAsync(new MagicAction { Kind = kind }, default));

    [Fact]
    public async Task Open_url_and_run_command()
    {
        var urls = new RecordingOpener();
        Assert.Equal("Opening teams.microsoft.com", await Make(urls: urls).RunAsync(new MagicAction { Kind = MagicActionKind.OpenUrl, Target = "https://teams.microsoft.com/x" }, default));
        Assert.Equal("https://teams.microsoft.com/x", Assert.Single(urls.Opened));

        var run = new FakeRunner();
        run.Results["xdotool key ctrl+shift+m"] = new(0, "toggled\nmore", "");
        Assert.Equal("toggled", await Make(run).RunAsync(new MagicAction { Kind = MagicActionKind.RunCommand, Target = "xdotool", Arguments = "key\nctrl+shift+m\n" }, default));

        run.Missing.Add("nope");
        var ex = await Assert.ThrowsAsync<ActionFailedException>(() => Make(run).RunAsync(new MagicAction { Kind = MagicActionKind.RunCommand, Target = "nope" }, default));
        Assert.Equal("Can't find nope", ex.Message);
    }
}

public class MagicButtonEngineTests : IAsyncLifetime
{
    private sealed class RecordingActions : IPcActions
    {
        public ConcurrentQueue<MagicActionKind> Ran { get; } = new();
        public Task<string> RunAsync(MagicAction action, CancellationToken ct)
        {
            Ran.Enqueue(action.Kind);
            if (action.Kind == MagicActionKind.ToggleSpeakerMute) throw new ActionFailedException("No speakers found");
            return Task.FromResult(action.Kind == MagicActionKind.ToggleMicMute ? "Mic muted" : "Play / pause");
        }
    }

    private readonly FakeDante _buddy = new();
    private readonly RecordingActions _actions = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
    private ConfigStore _store = null!;
    private DeviceLink _link = null!;
    private DanteEngine _engine = null!;

    public async Task InitializeAsync()
    {
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
        var cfg = new DanteConfig { Device = { Connection = ConnectionKind.Usb, PortName = "FAKE" } };
        cfg.Magic.Press = new MagicAction { Kind = MagicActionKind.PlayPause, ConfirmOnBuddy = false };
        cfg.Magic.LongPress = new MagicAction { Kind = MagicActionKind.ToggleMicMute };
        _store.Save(cfg);
        var push = new PushStore();
        _link = new DeviceLink(_store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => _buddy);
        _engine = new DanteEngine(_store, new SourceRegistry([new PushSource(push)]), _link, new RecordingOpener(), TimeProvider.System,
            NullLogger<DanteEngine>.Instance, push, new ClaudeCodeSessionTracker(TimeProvider.System), _actions);
        await _link.StartAsync(default);
        await _engine.StartAsync(default);
        for (var i = 0; i < 100 && !_buddy.Received.Any(m => m["t"]!.GetValue<string>() == "apps"); i++) await Task.Delay(50);
    }

    public async Task DisposeAsync()
    {
        await _engine.StopAsync(default);
        await _link.StopAsync(default);
        Directory.Delete(_dir, recursive: true);
    }

    private async Task<JsonObject?> WaitForSay(string text)
    {
        for (var i = 0; i < 60; i++)
        {
            var hit = _buddy.Received.FirstOrDefault(m => m["t"]!.GetValue<string>() == "say" && m["txt"]!.GetValue<string>() == text);
            if (hit is not null) return hit;
            await Task.Delay(50);
        }
        return null;
    }

    [Fact]
    public async Task Press_and_long_press_run_their_actions_and_confirm_on_the_buddy()
    {
        _buddy.Press("""{"t":"btn","b":"magic","long":false,"scr":"face"}""");
        _buddy.Press("""{"t":"btn","b":"magic","long":true,"scr":"face"}""");
        Assert.NotNull(await WaitForSay("Mic muted"));
        Assert.Equal([MagicActionKind.PlayPause, MagicActionKind.ToggleMicMute], _actions.Ran.ToArray());
        // Press has ConfirmOnBuddy = false: no bubble for it.
        Assert.DoesNotContain(_buddy.Received, m => m["t"]!.GetValue<string>() == "say" && m["txt"]!.GetValue<string>() == "Play / pause");
    }

    [Fact]
    public async Task Failures_are_always_shown_and_refresh_is_handled_by_the_engine()
    {
        var cfg = _store.Snapshot();
        cfg.Magic.Press = new MagicAction { Kind = MagicActionKind.ToggleSpeakerMute, ConfirmOnBuddy = false };
        cfg.Magic.LongPress = new MagicAction { Kind = MagicActionKind.RefreshApps };
        _store.Save(cfg);

        Assert.Equal("No speakers found", await _engine.RunMagicAsync(longPress: false));
        Assert.NotNull(await WaitForSay("No speakers found"));
        Assert.Equal("Refreshing apps!", await _engine.RunMagicAsync(longPress: true));
        Assert.DoesNotContain(MagicActionKind.RefreshApps, _actions.Ran);
    }
}
