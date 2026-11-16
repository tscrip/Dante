using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.Calendar;

/// <summary>"Next meeting" as a screen on DANTE: its name and time, and a countdown to it.</summary>
public sealed class NextMeetingSource(CalendarService calendar, TimeProvider time) : IWidgetType
{
    public const string TypeName = "calendar-next";
    public string Type => TypeName;
    public string PluginId => CalendarPlugin.PluginId;
    public bool AllowMultiple => false;
    public string DisplayName => "Next meeting";
    public string Description => "Counts down to your next meeting from the calendar set up on this page.";
    public IReadOnlyList<SettingField> Fields { get; } = [];
    public bool IsPushBased => true;

    public Task<WidgetReading> ReadAsync(WidgetDefinition app, CancellationToken ct)
    {
        if (calendar.Provider is not { IsReady: true })
            return Task.FromResult(new WidgetReading { Text = "Set up the calendar in the PC app" });
        if (calendar.Next is not { } next)
            return Task.FromResult(new WidgetReading { Text = "No more meetings today" });

        var now = time.GetUtcNow();
        var left = next.Start - now;
        var at = TimeZoneInfo.ConvertTime(next.Start, calendar.LocalZone).ToString("HH:mm");
        var title = string.IsNullOrWhiteSpace(next.Title) ? "Meeting" : next.Title.Trim();
        if (left <= TimeSpan.Zero)
            return Task.FromResult(new WidgetReading { Value = 0, Text = $"{title} - now!", Timer = new TimerReading(0, 3600, false) });

        var seconds = (int)Math.Ceiling(left.TotalSeconds);
        return Task.FromResult(new WidgetReading
        {
            Value = seconds,
            Text = $"{title} @ {at}",
            // The bar fills during the last hour before the meeting.
            Timer = new TimerReading(seconds, Math.Max(seconds, 3600), Running: true),
        });
    }

    public WidgetDefinition CreateDefault() => new()
    {
        Id = "next_meeting",
        Name = "Next meeting",
        Visual = VisualKind.Timer,
        Type = TypeName,
        Min = 0,
        Max = 3600,
        PollSeconds = 60,
        QuipsEnabled = false,
    };
}
