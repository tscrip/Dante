using Dante.Core.Config;
using Dante.Core.Sources;

namespace Dante.Server.Features.Calendar;

/// <summary>"Next meeting" as a screen on DANTE: its name and time, and a countdown to it.</summary>
public sealed class NextMeetingSource(CalendarService calendar, TimeProvider time) : IDataSource
{
    public const string TypeName = "calendar-next";
    public string Type => TypeName;
    public string DisplayName => "Next meeting (calendar)";
    public string Description => "Counts down to your next meeting from the calendar set up on the Calendar page.";
    public IReadOnlyList<SettingField> Settings { get; } = [];
    public bool IsPushBased => true;

    public Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct)
    {
        if (calendar.Provider is not { IsReady: true })
            return Task.FromResult(new SourceReading { Text = "Set up the calendar in the PC app" });
        if (calendar.Next is not { } next)
            return Task.FromResult(new SourceReading { Text = "No more meetings today" });

        var now = time.GetUtcNow();
        var left = next.Start - now;
        var at = TimeZoneInfo.ConvertTime(next.Start, calendar.LocalZone).ToString("HH:mm");
        var title = string.IsNullOrWhiteSpace(next.Title) ? "Meeting" : next.Title.Trim();
        if (left <= TimeSpan.Zero)
            return Task.FromResult(new SourceReading { Value = 0, Text = $"{title} - now!", Timer = new TimerReading(0, 3600, false) });

        var seconds = (int)Math.Ceiling(left.TotalSeconds);
        return Task.FromResult(new SourceReading
        {
            Value = seconds,
            Text = $"{title} @ {at}",
            // The bar fills during the last hour before the meeting.
            Timer = new TimerReading(seconds, Math.Max(seconds, 3600), Running: true),
        });
    }

    public static AppDefinition DefaultApp() => new()
    {
        Id = "next_meeting",
        Name = "Next meeting",
        Visual = VisualKind.Timer,
        SourceType = TypeName,
        Min = 0,
        Max = 3600,
        PollSeconds = 60,
        QuipsEnabled = false,
    };
}
