namespace Dante.Server.Features.Pomodoro;

public enum PomodoroPhase { Focus, ShortBreak, LongBreak }

/// <summary>A snapshot of the timer for the UI and DANTE.</summary>
/// <param name="Phase">The session that is running, paused, or up next.</param>
/// <param name="Running">The clock is ticking.</param>
/// <param name="Started">The session has started (it may be paused). False = waiting for a press to begin.</param>
/// <param name="Remaining">Time left in the session.</param>
/// <param name="Total">Length of the session.</param>
/// <param name="FocusDone">Focus sessions finished since the last long break (or reset).</param>
/// <param name="SessionsBeforeLongBreak">From the settings, for "2/4".</param>
public sealed record PomodoroState(
    PomodoroPhase Phase,
    bool Running,
    bool Started,
    TimeSpan Remaining,
    TimeSpan Total,
    int FocusDone,
    int SessionsBeforeLongBreak)
{
    /// <summary>Which focus session of the cycle this is (1-based), e.g. 2 of 4.</summary>
    public int FocusNumber => Math.Min(FocusDone + 1, SessionsBeforeLongBreak);
}

/// <summary>What happened when the clock ran out.</summary>
public sealed record PomodoroCompletion(PomodoroPhase Finished, PomodoroPhase Next, bool NextStarted, TimeSpan NextLength);

/// <summary>
/// The Pomodoro state machine: focus, short break, focus, ..., long break after every Nth focus session.
/// Pure logic: the caller passes the time in, which keeps it easy to test.
/// </summary>
public sealed class PomodoroTimer(PomodoroSettings settings)
{
    private PomodoroSettings _settings = settings.Normalized();
    private PomodoroPhase _phase = PomodoroPhase.Focus;
    private bool _started;
    private DateTimeOffset? _endsAt;          // set while running
    private TimeSpan _remaining = TimeSpan.FromMinutes(settings.Normalized().FocusMinutes);
    private TimeSpan _total = TimeSpan.FromMinutes(settings.Normalized().FocusMinutes);
    private int _focusDone;

    public bool Running => _endsAt is not null;

    public PomodoroState State(DateTimeOffset now) => new(
        _phase,
        Running,
        _started,
        _endsAt is { } end ? Max(end - now, TimeSpan.Zero) : _remaining,
        _total,
        _focusDone,
        _settings.SessionsBeforeLongBreak);

    /// <summary>New settings apply from the next session; a session that hasn't started picks up its new length.</summary>
    public void Configure(PomodoroSettings settings)
    {
        _settings = settings.Normalized();
        if (!_started) SetPhase(_phase);
    }

    /// <summary>SELECT on the timer screen: start, pause or resume.</summary>
    public void StartOrPause(DateTimeOffset now)
    {
        if (_endsAt is { } end)
        {
            _remaining = Max(end - now, TimeSpan.Zero);
            _endsAt = null;
        }
        else
        {
            _started = true;
            _endsAt = now + _remaining;
        }
    }

    /// <summary>Starts the session that is up next (or resumes a paused one). Does nothing while running.</summary>
    public void Start(DateTimeOffset now)
    {
        if (!Running) StartOrPause(now);
    }

    /// <summary>Back to the start of the cycle: focus session 1, not running.</summary>
    public void Reset()
    {
        _focusDone = 0;
        SetPhase(PomodoroPhase.Focus);
    }

    /// <summary>Ends the current session now, as if its time had run out.</summary>
    public PomodoroCompletion Skip(DateTimeOffset now) => Complete(now);

    /// <summary>Call regularly: returns what happened if the session just ran out.</summary>
    public PomodoroCompletion? Tick(DateTimeOffset now) =>
        _endsAt is { } end && now >= end ? Complete(now) : null;

    private PomodoroCompletion Complete(DateTimeOffset now)
    {
        var finished = _phase;
        PomodoroPhase next;
        if (finished == PomodoroPhase.Focus)
        {
            _focusDone++;
            next = _focusDone >= _settings.SessionsBeforeLongBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else
        {
            if (finished == PomodoroPhase.LongBreak) _focusDone = 0;   // a new cycle
            next = PomodoroPhase.Focus;
        }
        SetPhase(next);
        var autoStart = next == PomodoroPhase.Focus ? _settings.AutoStartFocus : _settings.AutoStartBreaks;
        if (autoStart) Start(now);
        return new PomodoroCompletion(finished, next, autoStart, _total);
    }

    private void SetPhase(PomodoroPhase phase)
    {
        _phase = phase;
        _started = false;
        _endsAt = null;
        _total = _remaining = TimeSpan.FromMinutes(phase switch
        {
            PomodoroPhase.Focus => _settings.FocusMinutes,
            PomodoroPhase.ShortBreak => _settings.ShortBreakMinutes,
            _ => _settings.LongBreakMinutes,
        });
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
