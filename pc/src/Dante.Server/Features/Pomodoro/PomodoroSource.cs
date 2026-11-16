using Dante.Core.Config;
using Dante.Core.Sources;

namespace Dante.Server.Features.Pomodoro;

/// <summary>The Pomodoro timer as a screen on DANTE: a countdown that DANTE ticks down by itself.</summary>
public sealed class PomodoroSource(PomodoroService pomodoro) : IDataSource
{
    public const string TypeName = "pomodoro";
    public string Type => TypeName;
    public string DisplayName => "Pomodoro timer";
    public string Description => "Focus and break countdown. On DANTE, press MAGIC on this screen to start or pause, hold it to reset. Set the lengths on the Pomodoro page.";
    public IReadOnlyList<SettingField> Settings { get; } = [];
    public bool IsPushBased => true;

    public Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct)
    {
        var s = pomodoro.State;
        var name = PomodoroService.PhaseName(s.Phase) + (s.Phase == PomodoroPhase.Focus ? $" {s.FocusNumber}/{s.SessionsBeforeLongBreak}" : "");
        var text = s.Running ? name
            : s.Started ? $"{name} - paused"
            : $"{name} - MAGIC: start";
        return Task.FromResult(new SourceReading
        {
            Value = Math.Ceiling(s.Remaining.TotalSeconds),
            Text = text,
            Timer = new TimerReading((int)Math.Ceiling(s.Remaining.TotalSeconds), (int)s.Total.TotalSeconds, s.Running),
        });
    }

    /// <summary>The app entry that puts the timer in DANTE's carousel.</summary>
    public static AppDefinition DefaultApp() => new()
    {
        Id = "pomodoro",
        Name = "Pomodoro",
        Visual = VisualKind.Timer,
        Min = 0,
        Max = 25 * 60,             // only used by the editor preview: DANTE gets the real length with each value
        SourceType = TypeName,
        PollSeconds = 60,          // a safety net: changes are pushed straight away
        QuipsEnabled = false,
    };
}
