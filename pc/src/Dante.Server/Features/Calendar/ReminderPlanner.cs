using Dante.Core.Behavior;

namespace Dante.Server.Features.Calendar;

/// <summary>A reminder that is due now.</summary>
/// <param name="Event">The meeting.</param>
/// <param name="Offset">The configured reminder (minutes before the start) that fired.</param>
/// <param name="MinutesLeft">Actual whole minutes until the start (rounded up), 0 once it has started.</param>
/// <param name="IsLast">No later reminder is configured for this meeting.</param>
public sealed record DueReminder(CalendarEvent Event, int Offset, int MinutesLeft, bool IsLast);

/// <summary>
/// Decides which meeting reminders are due. Pure logic (time and history come in), so it is easy to test.
/// A reminder fires once per meeting occurrence and offset; if DANTE was off and several offsets are
/// overdue, only the most recent one fires. Reminders stop once a meeting has been going for a few minutes.
/// </summary>
public static class ReminderPlanner
{
    /// <summary>How long after the start a late "starting now" reminder still makes sense.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    public static string Key(CalendarEvent e, int offset) => $"{e.OccurrenceKey}|{offset}";

    /// <param name="fired">Keys from <see cref="Key"/> that already fired; due ones are added.</param>
    /// <param name="dismissed">Occurrence keys the human acknowledged: no more reminders for them.</param>
    public static IReadOnlyList<DueReminder> Due(IEnumerable<CalendarEvent> events, CalendarSettings settings, DateTimeOffset now,
        ISet<string> fired, IReadOnlySet<string> dismissed)
    {
        var offsets = settings.ReminderMinutes.Distinct().OrderDescending().ToList();
        if (offsets.Count == 0) return [];
        var due = new List<DueReminder>();
        foreach (var e in events.Where(settings.Wants))
        {
            if (dismissed.Contains(e.OccurrenceKey) || now >= e.Start + Grace || now >= e.End) continue;
            // The smallest offset whose moment has passed is the one that matters now.
            var passed = offsets.Where(m => now >= e.Start - TimeSpan.FromMinutes(m)).ToList();
            if (passed.Count == 0) continue;
            var offset = passed.Min();
            var fresh = !fired.Contains(Key(e, offset));
            foreach (var m in passed) fired.Add(Key(e, m));   // overdue earlier reminders are skipped, not replayed
            if (!fresh) continue;
            var minutesLeft = Math.Max(0, (int)Math.Ceiling((e.Start - now).TotalMinutes));
            due.Add(new DueReminder(e, offset, minutesLeft, IsLast: offset == offsets.Min()));
        }
        return [.. due.OrderBy(d => d.Event.Start)];
    }

    /// <summary>The reminder's text for DANTE.</summary>
    public static string Text(DueReminder r, CalendarSettings settings, TimeZoneInfo zone)
    {
        var template = r.MinutesLeft == 0 ? settings.StartingMessage : settings.Message;
        return Templates.Render(template, new Dictionary<string, string>
        {
            ["title"] = string.IsNullOrWhiteSpace(r.Event.Title) ? "Meeting" : r.Event.Title.Trim(),
            ["minutes"] = r.MinutesLeft.ToString(),
            ["time"] = TimeZoneInfo.ConvertTime(r.Event.Start, zone).ToString("HH:mm"),
            ["location"] = r.Event.Location ?? "",
        });
    }
}
