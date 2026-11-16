namespace Dante.Server.Features.Calendar;

/// <summary>Calendar reminders, stored in config.json under features.calendar.</summary>
public sealed class CalendarSettings
{
    public const string Key = "calendar";

    public bool Enabled { get; set; }
    /// <summary>Which <see cref="ICalendarProvider"/> to read.</summary>
    public string Provider { get; set; } = Microsoft365CalendarProvider.ProviderId;
    /// <summary>Remind this many minutes before each meeting, e.g. 10, 5 and 1. 0 = when it starts.</summary>
    public List<int> ReminderMinutes { get; set; } = [10, 1];
    /// <summary>How often to fetch the calendar.</summary>
    public int SyncMinutes { get; set; } = 5;

    public bool SkipAllDay { get; set; } = true;
    public bool SkipDeclined { get; set; } = true;
    /// <summary>Events marked "free" (holds, focus blocks you don't mind missing...).</summary>
    public bool SkipFree { get; set; } = true;
    public bool SkipTentative { get; set; }

    /// <summary>1 = nudge, 2 = urgent (the screen flashes).</summary>
    public int NudgeLevel { get; set; } = 1;
    /// <summary>The last reminder before a meeting is urgent, whatever <see cref="NudgeLevel"/> says.</summary>
    public bool LastReminderUrgent { get; set; } = true;
    /// <summary>SELECT on the reminder opens the meeting's Teams / Zoom link on the PC.</summary>
    public bool OpenJoinLinkOnSelect { get; set; } = true;
    /// <summary>Tokens: {title} {minutes} {time} {location}.</summary>
    public string Message { get; set; } = "{title} in {minutes} min";
    /// <summary>Used for the "starting now" reminder (0 minutes).</summary>
    public string StartingMessage { get; set; } = "{title} is starting now!";

    public CalendarSettings Normalized() => new()
    {
        Enabled = Enabled,
        Provider = string.IsNullOrWhiteSpace(Provider) ? Microsoft365CalendarProvider.ProviderId : Provider,
        ReminderMinutes = [.. ReminderMinutes.Select(m => Math.Clamp(m, 0, 240)).Distinct().OrderDescending()],
        SyncMinutes = Math.Clamp(SyncMinutes, 1, 60),
        SkipAllDay = SkipAllDay,
        SkipDeclined = SkipDeclined,
        SkipFree = SkipFree,
        SkipTentative = SkipTentative,
        NudgeLevel = Math.Clamp(NudgeLevel, 1, 2),
        LastReminderUrgent = LastReminderUrgent,
        OpenJoinLinkOnSelect = OpenJoinLinkOnSelect,
        Message = string.IsNullOrWhiteSpace(Message) ? "{title} in {minutes} min" : Message,
        StartingMessage = string.IsNullOrWhiteSpace(StartingMessage) ? "{title} is starting now!" : StartingMessage,
    };

    /// <summary>Does this meeting get reminders at all?</summary>
    public bool Wants(CalendarEvent e) =>
        !e.IsCancelled
        && !(SkipAllDay && e.IsAllDay)
        && !(SkipDeclined && e.Response == ResponseStatus.Declined)
        && !(SkipFree && e.ShowAs == ShowAs.Free)
        && !(SkipTentative && (e.Response == ResponseStatus.Tentative || e.ShowAs == ShowAs.Tentative));
}
