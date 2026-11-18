using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;
using Dante.Core.Widgets;
using Dante.Server.Features.Pomodoro;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Tests;

public class PomodoroTimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static PomodoroTimer Timer(Action<PomodoroSettings>? tweak = null)
    {
        var s = new PomodoroSettings { FocusMinutes = 25, ShortBreakMinutes = 5, LongBreakMinutes = 15, SessionsBeforeLongBreak = 2 };
        tweak?.Invoke(s);
        return new PomodoroTimer(s);
    }

    [Fact]
    public void Starts_ready_with_a_full_focus_session()
    {
        var s = Timer().State(T0);
        Assert.Equal(PomodoroPhase.Focus, s.Phase);
        Assert.False(s.Running);
        Assert.False(s.Started);
        Assert.Equal(TimeSpan.FromMinutes(25), s.Remaining);
        Assert.Equal(1, s.FocusNumber);
    }

    [Fact]
    public void Pause_keeps_the_time_left_and_resume_continues_from_there()
    {
        var t = Timer();
        t.StartOrPause(T0);
        t.StartOrPause(T0.AddMinutes(10));             // pause after 10 minutes
        var paused = t.State(T0.AddMinutes(30));       // time passes while paused
        Assert.False(paused.Running);
        Assert.True(paused.Started);
        Assert.Equal(TimeSpan.FromMinutes(15), paused.Remaining);

        t.StartOrPause(T0.AddMinutes(30));             // resume
        Assert.Equal(TimeSpan.FromMinutes(10), t.State(T0.AddMinutes(35)).Remaining);
        Assert.Null(t.Tick(T0.AddMinutes(44)));
        Assert.NotNull(t.Tick(T0.AddMinutes(45)));
    }

    [Fact]
    public void Focus_then_short_break_then_long_break_after_the_nth_session()
    {
        var t = Timer();
        t.StartOrPause(T0);
        var first = t.Tick(T0.AddMinutes(25))!;
        Assert.Equal(new PomodoroCompletion(PomodoroPhase.Focus, PomodoroPhase.ShortBreak, false, TimeSpan.FromMinutes(5)), first);
        Assert.False(t.Running);                       // waits for a press

        t.Start(T0.AddMinutes(26));
        Assert.Equal(PomodoroPhase.Focus, t.Tick(T0.AddMinutes(31))!.Next);
        t.Start(T0.AddMinutes(31));
        var second = t.Tick(T0.AddMinutes(56))!;
        Assert.Equal(PomodoroPhase.LongBreak, second.Next);   // 2 sessions before a long break
        Assert.Equal(TimeSpan.FromMinutes(15), second.NextLength);

        t.Start(T0.AddMinutes(56));
        t.Tick(T0.AddMinutes(71));
        Assert.Equal(0, t.State(T0.AddMinutes(71)).FocusDone);   // a new cycle
    }

    [Fact]
    public void Auto_start_rolls_straight_into_the_next_session()
    {
        var t = Timer(s => s.AutoStartBreaks = true);
        t.StartOrPause(T0);
        var done = t.Tick(T0.AddMinutes(25))!;
        Assert.True(done.NextStarted);
        Assert.True(t.Running);
        Assert.Equal(TimeSpan.FromMinutes(4), t.State(T0.AddMinutes(26)).Remaining);
        // ...but focus doesn't auto-start unless that is switched on too.
        Assert.False(t.Tick(T0.AddMinutes(30))!.NextStarted);
    }

    [Fact]
    public void Reset_goes_back_to_focus_one_and_new_lengths_apply_to_sessions_not_yet_started()
    {
        var t = Timer();
        t.StartOrPause(T0);
        t.Skip(T0.AddMinutes(3));
        Assert.Equal(PomodoroPhase.ShortBreak, t.State(T0).Phase);

        t.Configure(new PomodoroSettings { FocusMinutes = 50, ShortBreakMinutes = 10 });
        Assert.Equal(TimeSpan.FromMinutes(10), t.State(T0).Remaining);   // the break hadn't started

        t.Reset();
        var s = t.State(T0);
        Assert.Equal(PomodoroPhase.Focus, s.Phase);
        Assert.Equal(TimeSpan.FromMinutes(50), s.Remaining);
        Assert.Equal(0, s.FocusDone);
    }

    [Fact]
    public void Silly_settings_are_clamped()
    {
        var s = new PomodoroSettings { FocusMinutes = 0, SessionsBeforeLongBreak = 99, NudgeLevel = 7 }.Normalized();
        Assert.Equal(1, s.FocusMinutes);
        Assert.Equal(12, s.SessionsBeforeLongBreak);
        Assert.Equal(2, s.NudgeLevel);
    }
}

public class PomodoroOnDanteTests : IAsyncLifetime
{
    private sealed class MagicSpy : IDeviceEventHandler
    {
        public ConcurrentQueue<string?> Screens { get; } = new();
        public bool Handle(DeviceEvent evt, WidgetDefinition? screen)
        {
            if (evt.Type != "btn" || evt.Button != "magic") return false;
            Screens.Enqueue(screen?.Id ?? "face");
            return true;
        }
    }

    private readonly FakeDante _buddy = new();
    private readonly MagicSpy _magic = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
    private ConfigStore _store = null!;
    private DeviceLink _link = null!;
    private DanteEngine _engine = null!;
    private PomodoroService _pomodoro = null!;

    public async Task InitializeAsync()
    {
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
        var cfg = new DanteConfig { Device = { Connection = ConnectionKind.Usb, PortName = "FAKE" }, Widgets = [new PomodoroSource(null!).CreateDefault()] };
        cfg.SetFeature(PomodoroSettings.Key, new PomodoroSettings { FocusMinutes = 20, SessionsBeforeLongBreak = 3 });
        _store.Save(cfg);

        _link = new DeviceLink(_store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => _buddy);
        var alerts = new DeviceAlerts(_link);
        _pomodoro = new PomodoroService(_store, alerts, TimeProvider.System, NullLogger<PomodoroService>.Instance);
        var registry = new WidgetTypeRegistry([new PomodoroSource(_pomodoro)]);
        _engine = new DanteEngine(_store, registry, _link, new RecordingOpener(), TimeProvider.System, NullLogger<DanteEngine>.Instance, alerts);
        _engine.AddHandler(_magic, 1000);   // stands in for the MAGIC button's PC action
        _pomodoro.Initialize(_engine);   // what PomodoroFeature.Start does
        await _link.StartAsync(default);
        await _engine.StartAsync(default);
        await WaitFor(m => Is(m, "apps"));
    }

    public async Task DisposeAsync()
    {
        await _engine.StopAsync(default);
        await _link.StopAsync(default);
        Directory.Delete(_dir, recursive: true);
    }

    private async Task<JsonObject> WaitFor(Func<JsonObject, bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            var hit = _buddy.Received.FirstOrDefault(predicate);
            if (hit is not null) return hit;
            await Task.Delay(50);
        }
        throw new TimeoutException("Expected message never sent. Got: " + string.Join("\n", _buddy.Received.Select(m => m.ToJsonString())));
    }

    private static bool Is(JsonObject m, string type) => m["t"]!.GetValue<string>() == type;

    [Fact]
    public async Task The_timer_screen_is_a_countdown_and_SELECT_on_it_starts_and_pauses()
    {
        var apps = await WaitFor(m => Is(m, "apps"));
        Assert.Equal("timer", apps["apps"]![0]!["vis"]!.GetValue<string>());
        var ready = await WaitFor(m => Is(m, "val") && m["id"]!.GetValue<string>() == "pomodoro");
        Assert.Equal(1200, ready["v"]!.GetValue<int>());
        Assert.Equal(1200, ready["tot"]!.GetValue<int>());
        Assert.False(ready["run"]!.GetValue<bool>());
        Assert.Equal("Focus 1/3 - SEL: start", ready["txt"]!.GetValue<string>());

        _buddy.Press("""{"t":"btn","b":"sel","long":false,"scr":"pomodoro"}""");
        await WaitFor(m => Is(m, "say") && m["txt"]!.GetValue<string>().StartsWith("Focus 1/3: 20:00", StringComparison.Ordinal));
        await WaitFor(m => Is(m, "val") && m["run"]?.GetValue<bool>() == true);
        Assert.True(_pomodoro.State.Running);

        _buddy.Press("""{"t":"btn","b":"sel","long":false,"scr":"pomodoro"}""");
        await WaitFor(m => Is(m, "say") && m["txt"]!.GetValue<string>().StartsWith("Paused", StringComparison.Ordinal));
        Assert.False(_pomodoro.State.Running);
    }

    [Fact]
    public async Task Long_SELECT_on_the_timer_resets_it_instead_of_refreshing_everything()
    {
        _pomodoro.StartOrPause();
        _buddy.Press("""{"t":"btn","b":"sel","long":true,"scr":"pomodoro"}""");
        await WaitFor(m => Is(m, "say") && m["txt"]!.GetValue<string>() == "Pomodoro reset.");
        Assert.False(_pomodoro.State.Started);
        Assert.DoesNotContain(_buddy.Received, m => Is(m, "say") && m["txt"]!.GetValue<string>() == "Refreshing everything!");
    }

    [Theory]
    [InlineData("pomodoro", false)]
    [InlineData("pomodoro", true)]
    [InlineData("face", false)]
    public async Task MAGIC_always_runs_the_PC_action_even_on_the_timer(string screen, bool longPress)
    {
        var json = $$"""{"t":"btn","b":"magic","long":{{(longPress ? "true" : "false")}},"scr":"{{screen}}"}""";
        _buddy.Press(json);
        for (var i = 0; i < 40 && _magic.Screens.IsEmpty; i++) await Task.Delay(50);
        Assert.Equal(screen, Assert.Single(_magic.Screens));
        Assert.False(_pomodoro.State.Started);   // the timer was not touched
    }

    [Fact]
    public async Task A_finished_session_nudges_and_SELECT_starts_the_break()
    {
        _pomodoro.StartOrPause();
        _pomodoro.Skip();   // as if the 20 minutes were up
        var alert = await WaitFor(m => Is(m, "alert") && m["id"]!.GetValue<string>() == PomodoroService.AlertId);
        Assert.Equal("Focus done! Break: 5 min. SEL: start", alert["txt"]!.GetValue<string>());
        Assert.False(_pomodoro.State.Running);

        _buddy.Press("""{"t":"ack","id":"pomodoro"}""");
        for (var i = 0; i < 40 && !_pomodoro.State.Running; i++) await Task.Delay(50);
        Assert.True(_pomodoro.State.Running);
        Assert.Equal(PomodoroPhase.ShortBreak, _pomodoro.State.Phase);
    }
}
