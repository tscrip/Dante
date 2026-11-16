namespace Dante.Server.Features.Pomodoro;

/// <summary>The Pomodoro timer's settings, stored in config.json under features.pomodoro.</summary>
public sealed class PomodoroSettings
{
    public const string Key = "pomodoro";

    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    /// <summary>Every Nth focus session is followed by a long break.</summary>
    public int SessionsBeforeLongBreak { get; set; } = 4;
    /// <summary>Start the break as soon as a focus session ends (otherwise press MAGIC or SELECT).</summary>
    public bool AutoStartBreaks { get; set; }
    /// <summary>Start the next focus session as soon as a break ends.</summary>
    public bool AutoStartFocus { get; set; }
    /// <summary>When a session ends, take over DANTE's screen until you answer (otherwise just a speech bubble).</summary>
    public bool NudgeWhenDone { get; set; } = true;
    /// <summary>1 = nudge, 2 = urgent (the screen flashes).</summary>
    public int NudgeLevel { get; set; } = 1;

    /// <summary>Clamps everything to sensible ranges.</summary>
    public PomodoroSettings Normalized() => new()
    {
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180),
        ShortBreakMinutes = Math.Clamp(ShortBreakMinutes, 1, 60),
        LongBreakMinutes = Math.Clamp(LongBreakMinutes, 1, 120),
        SessionsBeforeLongBreak = Math.Clamp(SessionsBeforeLongBreak, 1, 12),
        AutoStartBreaks = AutoStartBreaks,
        AutoStartFocus = AutoStartFocus,
        NudgeWhenDone = NudgeWhenDone,
        NudgeLevel = Math.Clamp(NudgeLevel, 1, 2),
    };
}
