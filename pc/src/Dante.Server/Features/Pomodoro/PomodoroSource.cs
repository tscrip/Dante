using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.Pomodoro;

/// <summary>The Pomodoro timer as a screen on DANTE: a countdown that DANTE ticks down by itself.</summary>
public sealed class PomodoroSource(PomodoroService pomodoro) : IWidgetType
{
    public const string TypeName = "pomodoro";
    public string Type => TypeName;
    public string PluginId => PomodoroPlugin.PluginId;
    public bool AllowMultiple => false;
    public string DisplayName => "Timer";
    public string Description => "Focus and break countdown. On DANTE, press SELECT on this screen to start or pause, hold it to reset. Set the lengths on this page.";
    public IReadOnlyList<SettingField> Fields { get; } = [];
    public bool IsPushBased => true;

    public Task<WidgetReading> ReadAsync(WidgetDefinition app, CancellationToken ct)
    {
        var s = pomodoro.State;
        var name = PomodoroService.PhaseName(s.Phase) + (s.Phase == PomodoroPhase.Focus ? $" {s.FocusNumber}/{s.SessionsBeforeLongBreak}" : "");
        var text = s.Running ? name
            : s.Started ? $"{name} - paused"
            : $"{name} - SEL: start";
        return Task.FromResult(new WidgetReading
        {
            Value = Math.Ceiling(s.Remaining.TotalSeconds),
            Text = text,
            Timer = new TimerReading((int)Math.Ceiling(s.Remaining.TotalSeconds), (int)s.Total.TotalSeconds, s.Running),
        });
    }

    public WidgetDefinition CreateDefault() => new()
    {
        Id = "pomodoro",
        Name = "Pomodoro",
        Visual = VisualKind.Timer,
        Min = 0,
        Max = 25 * 60,             // only used by the editor preview: DANTE gets the real length with each value
        Type = TypeName,
        PollSeconds = 60,          // a safety net: changes are pushed straight away
        QuipsEnabled = false,
    };
}
