using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;

namespace Dante.Server.Features.Pomodoro;

/// <summary>
/// Runs the Pomodoro timer: ticks it, tells DANTE when a session ends, and owns the MAGIC button while
/// the Pomodoro screen is showing (press = start / pause, long press = reset). SELECT on the
/// "session done" nudge starts the next session.
/// </summary>
public sealed class PomodoroService(ConfigStore config, DeviceAlerts alerts, TimeProvider time, ILogger<PomodoroService> log) : BackgroundService, IDeviceEventHandler
{
    public const string AlertId = "pomodoro";
    /// <summary>Before catch-all handlers such as the MAGIC button's PC action.</summary>
    public const int HandlerOrder = 100;

    private readonly Lock _gate = new();
    // Set by Initialize, not injected: the engine needs the data sources, and PomodoroSource needs this service.
    private DanteEngine? _engine;
    private readonly PomodoroTimer _timer = new(config.Snapshot().Feature<PomodoroSettings>(PomodoroSettings.Key));

    /// <summary>The timer changed (started, paused, a session ended); the UI re-renders.</summary>
    public event Action? Changed;

    public PomodoroState State
    {
        get { lock (_gate) return _timer.State(time.GetUtcNow()); }
    }

    public PomodoroSettings Settings => config.Snapshot().Feature<PomodoroSettings>(PomodoroSettings.Key).Normalized();

    /// <summary>Hooks the timer up to the engine and the config; called once at start-up.</summary>
    public void Initialize(DanteEngine engine)
    {
        _engine = engine;
        config.Changed += cfg =>
        {
            lock (_gate) _timer.Configure(cfg.Feature<PomodoroSettings>(PomodoroSettings.Key));
            Notify();
        };
        alerts.Acknowledged += a => { if (a.Id == AlertId) Start(); };   // SELECT on "Focus done!": start the break
        engine.AddHandler(this, HandlerOrder);
    }

    /// <summary>MAGIC on the Pomodoro screen, or the Start/Pause button in the UI.</summary>
    public void StartOrPause()
    {
        PomodoroState state;
        lock (_gate)
        {
            _timer.StartOrPause(time.GetUtcNow());
            state = _timer.State(time.GetUtcNow());
        }
        alerts.Clear(AlertId);
        alerts.Say(state.Running ? StartedLine(state) : $"Paused at {Clock(state.Remaining)}.");
        Notify();
    }

    public void Start()
    {
        PomodoroState state;
        lock (_gate)
        {
            if (_timer.Running) return;
            _timer.Start(time.GetUtcNow());
            state = _timer.State(time.GetUtcNow());
        }
        alerts.Clear(AlertId);
        alerts.Say(StartedLine(state));
        Notify();
    }

    /// <summary>Long-press MAGIC on the Pomodoro screen, or Reset in the UI.</summary>
    public void Reset()
    {
        lock (_gate) _timer.Reset();
        alerts.Clear(AlertId);
        alerts.Say("Pomodoro reset.");
        Notify();
    }

    /// <summary>Ends the current session now (UI only).</summary>
    public void Skip()
    {
        PomodoroCompletion done;
        lock (_gate) done = _timer.Skip(time.GetUtcNow());
        Announce(done);
        Notify();
    }

    public bool Handle(DeviceEvent evt, AppDefinition? screen)
    {
        if (evt.Type != "btn" || evt.Button != "magic" || screen?.SourceType != PomodoroSource.TypeName) return false;
        if (evt.Long) Reset(); else StartOrPause();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await tick.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                PomodoroCompletion? done;
                lock (_gate) done = _timer.Tick(time.GetUtcNow());
                if (done is null) continue;
                Announce(done);
                Notify();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Pomodoro tick failed");
            }
        }
    }

    private void Announce(PomodoroCompletion done)
    {
        var settings = Settings;
        var minutes = (int)Math.Round(done.NextLength.TotalMinutes);
        var text = (done.Finished, done.Next) switch
        {
            (PomodoroPhase.Focus, PomodoroPhase.LongBreak) => $"Cycle done! Long break: {minutes} min.",
            (PomodoroPhase.Focus, _) => $"Focus done! Break: {minutes} min.",
            _ => $"Break's over. Focus: {minutes} min.",
        };
        if (done.NextStarted || !settings.NudgeWhenDone)
            alerts.Say(done.NextStarted ? text : text + " MAGIC: start");
        else
            alerts.Raise(AlertId, "Pomodoro", text + " SEL: start", settings.NudgeLevel);
    }

    private void Notify()
    {
        _engine?.PollSourceType(PomodoroSource.TypeName);   // DANTE gets the new clock right away
        Changed?.Invoke();
    }

    private static string StartedLine(PomodoroState s) => s.Phase switch
    {
        PomodoroPhase.Focus => $"Focus {s.FocusNumber}/{s.SessionsBeforeLongBreak}: {Clock(s.Remaining)}. Go!",
        PomodoroPhase.LongBreak => $"Long break: {Clock(s.Remaining)}. Stretch!",
        _ => $"Break: {Clock(s.Remaining)}. Look away from the screen!",
    };

    /// <summary>"24:59": rounded up, like any countdown (a just-started 20 minute session reads 20:00).</summary>
    public static string Clock(TimeSpan t)
    {
        var secs = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds));
        return $"{secs / 60}:{secs % 60:00}";
    }

    public static string PhaseName(PomodoroPhase p) => p switch
    {
        PomodoroPhase.Focus => "Focus",
        PomodoroPhase.ShortBreak => "Break",
        _ => "Long break",
    };
}
