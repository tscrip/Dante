namespace Dante.Server.Features.Calendar;

/// <summary>
/// A calendar service DANTE can read meetings from. Outlook / Microsoft 365 is the first; another service
/// (Google, an ICS feed, ...) is a new class implementing this, registered in <see cref="CalendarFeature"/>.
/// </summary>
public interface ICalendarProvider
{
    /// <summary>Stable id stored in the settings, e.g. "microsoft365".</summary>
    string Id { get; }
    string DisplayName { get; }
    /// <summary>
    /// A Razor component with the provider's own set-up (sign-in, account, options), shown on the Calendar
    /// page. It gets the provider as a <c>Provider</c> parameter.
    /// </summary>
    Type SetupComponent { get; }
    /// <summary>Signed in / configured enough to read events.</summary>
    bool IsReady { get; }
    /// <summary>Who or what is connected, for the UI ("tyler@contoso.com"), or null.</summary>
    string? AccountName { get; }

    /// <summary>Events that overlap [from, to), in any order.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public enum ResponseStatus { None, Organizer, Accepted, Tentative, Declined, NotResponded }

public enum ShowAs { Unknown, Free, Tentative, Busy, OutOfOffice, WorkingElsewhere }

/// <summary>One meeting, normalised across providers. Times are absolute (UTC offsets included).</summary>
public sealed record CalendarEvent(
    string Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay = false,
    string? Location = null,
    /// <summary>Teams / Zoom / Meet link, if the meeting has one.</summary>
    string? JoinUrl = null,
    /// <summary>The event in the calendar's web UI.</summary>
    string? WebLink = null,
    ResponseStatus Response = ResponseStatus.None,
    ShowAs ShowAs = ShowAs.Busy,
    bool IsCancelled = false)
{
    /// <summary>Identifies one occurrence: a moved meeting gets fresh reminders.</summary>
    public string OccurrenceKey => $"{Id}|{Start.UtcDateTime:O}";
}
