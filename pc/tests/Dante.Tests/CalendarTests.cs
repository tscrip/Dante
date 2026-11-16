using System.Text.Json.Nodes;
using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Server.Features.Calendar;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Tests;

public class ReminderPlannerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly CalendarEvent Standup = new("e1", "Standup", Start, Start.AddMinutes(15));
    private static readonly CalendarSettings Settings = new CalendarSettings { Enabled = true, ReminderMinutes = [10, 1] }.Normalized();

    private static IReadOnlyList<DueReminder> Due(DateTimeOffset now, HashSet<string> fired, IEnumerable<CalendarEvent>? events = null, IReadOnlySet<string>? dismissed = null) =>
        ReminderPlanner.Due(events ?? [Standup], Settings, now, fired, dismissed ?? new HashSet<string>());

    [Fact]
    public void Each_reminder_fires_once_at_its_time()
    {
        var fired = new HashSet<string>();
        Assert.Empty(Due(Start.AddMinutes(-11), fired));
        var ten = Assert.Single(Due(Start.AddMinutes(-10), fired));
        Assert.Equal((10, 10, false), (ten.Offset, ten.MinutesLeft, ten.IsLast));
        Assert.Empty(Due(Start.AddMinutes(-9), fired));            // not again
        var one = Assert.Single(Due(Start.AddSeconds(-50), fired));
        Assert.Equal((1, 1, true), (one.Offset, one.MinutesLeft, one.IsLast));
        Assert.Empty(Due(Start.AddMinutes(2), fired));
    }

    [Fact]
    public void Starting_late_fires_only_the_most_recent_reminder_with_the_real_minutes_left()
    {
        var fired = new HashSet<string>();
        var r = Assert.Single(Due(Start.AddMinutes(-3), fired));   // DANTE came online 3 minutes before
        Assert.Equal(10, r.Offset);
        Assert.Equal(3, r.MinutesLeft);
        Assert.Equal("Standup in 3 min", ReminderPlanner.Text(r, Settings, TimeZoneInfo.Utc));

        fired.Clear();
        var late = Assert.Single(Due(Start.AddSeconds(-30), fired));   // both overdue: only the 1-minute one
        Assert.Equal(1, late.Offset);
        Assert.Empty(Due(Start.AddSeconds(-20), fired));
    }

    [Fact]
    public void A_moved_meeting_gets_fresh_reminders_and_a_dismissed_one_gets_none()
    {
        var fired = new HashSet<string>();
        Assert.Single(Due(Start.AddMinutes(-10), fired));
        var moved = Standup with { Start = Start.AddMinutes(30), End = Start.AddMinutes(45) };
        Assert.Single(Due(Start.AddMinutes(20), fired, [moved]));

        Assert.Empty(Due(Start.AddMinutes(-1), new HashSet<string>(), dismissed: new HashSet<string> { Standup.OccurrenceKey }));
    }

    [Fact]
    public void Starting_now_uses_its_own_message_and_nothing_fires_once_it_is_well_under_way()
    {
        var settings = new CalendarSettings { ReminderMinutes = [0] }.Normalized();
        var r = Assert.Single(ReminderPlanner.Due([Standup], settings, Start.AddSeconds(5), new HashSet<string>(), new HashSet<string>()));
        Assert.Equal("Standup is starting now!", ReminderPlanner.Text(r, settings, TimeZoneInfo.Utc));
        Assert.Empty(ReminderPlanner.Due([Standup], settings, Start.AddMinutes(6), new HashSet<string>(), new HashSet<string>()));
    }

    [Fact]
    public void Message_tokens_and_local_time()
    {
        var settings = new CalendarSettings { Message = "{title} at {time} in {location} ({minutes}m)" }.Normalized();
        var zone = TimeZoneInfo.CreateCustomTimeZone("Plus2", TimeSpan.FromHours(2), "Plus2", "Plus2");
        var r = new DueReminder(Standup with { Location = "Room 4" }, 5, 5, false);
        Assert.Equal("Standup at 12:00 in Room 4 (5m)", ReminderPlanner.Text(r, settings, zone));
    }

    [Theory]
    [InlineData(true, false, ResponseStatus.Accepted, ShowAs.Busy, false)]       // all-day
    [InlineData(false, true, ResponseStatus.Accepted, ShowAs.Busy, false)]       // cancelled
    [InlineData(false, false, ResponseStatus.Declined, ShowAs.Busy, false)]
    [InlineData(false, false, ResponseStatus.Accepted, ShowAs.Free, false)]
    [InlineData(false, false, ResponseStatus.Tentative, ShowAs.Tentative, true)] // tentative is kept by default
    [InlineData(false, false, ResponseStatus.Organizer, ShowAs.Busy, true)]
    public void Default_filters(bool allDay, bool cancelled, ResponseStatus response, ShowAs showAs, bool wanted) =>
        Assert.Equal(wanted, new CalendarSettings().Wants(Standup with { IsAllDay = allDay, IsCancelled = cancelled, Response = response, ShowAs = showAs }));

    [Fact]
    public void Settings_are_normalised()
    {
        var s = new CalendarSettings { ReminderMinutes = [1, 10, 10, -5, 999], SyncMinutes = 0, Message = " " }.Normalized();
        Assert.Equal([240, 10, 1, 0], s.ReminderMinutes);
        Assert.Equal(1, s.SyncMinutes);
        Assert.Equal("{title} in {minutes} min", s.Message);
    }
}

public class GraphParsingTests
{
    [Fact]
    public void Parses_a_calendarView_page()
    {
        var page = JsonNode.Parse("""
        {
          "value": [
            {
              "id": "AAMk1", "subject": "Design review",
              "start": { "dateTime": "2026-11-16T15:00:00.0000000", "timeZone": "UTC" },
              "end":   { "dateTime": "2026-11-16T15:30:00.0000000", "timeZone": "UTC" },
              "isAllDay": false, "isCancelled": false, "showAs": "busy",
              "location": { "displayName": "Room 4" },
              "onlineMeeting": { "joinUrl": "https://teams.microsoft.com/l/meetup-join/abc" },
              "webLink": "https://outlook.office365.com/owa/?itemid=AAMk1",
              "responseStatus": { "response": "tentativelyAccepted" }
            },
            {
              "id": "AAMk2", "subject": "Holiday",
              "start": { "dateTime": "2026-11-17T00:00:00.0000000", "timeZone": "UTC" },
              "end":   { "dateTime": "2026-11-18T00:00:00.0000000", "timeZone": "UTC" },
              "isAllDay": true, "showAs": "oof", "location": { "displayName": "" }, "onlineMeeting": null,
              "responseStatus": { "response": "organizer" }
            },
            { "id": "broken", "subject": "no times" }
          ],
          "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/calendarView?$skip=100"
        }
        """);
        var events = Microsoft365CalendarProvider.ParseEvents(page).ToList();
        Assert.Equal(2, events.Count);

        var review = events[0];
        Assert.Equal("Design review", review.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero), review.Start);
        Assert.Equal(TimeSpan.FromMinutes(30), review.End - review.Start);
        Assert.Equal("Room 4", review.Location);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", review.JoinUrl);
        Assert.Equal(ResponseStatus.Tentative, review.Response);
        Assert.Equal(ShowAs.Busy, review.ShowAs);

        var holiday = events[1];
        Assert.True(holiday.IsAllDay);
        Assert.Null(holiday.Location);
        Assert.Null(holiday.JoinUrl);
        Assert.Equal(ResponseStatus.Organizer, holiday.Response);
        Assert.Equal(ShowAs.OutOfOffice, holiday.ShowAs);
    }
}

public class CalendarServiceTests : IDisposable
{
    private sealed class FakeCalendar : ICalendarProvider
    {
        public List<CalendarEvent> Events { get; } = [];
        public int Fetches { get; private set; }
        public string Id => "fake";
        public string DisplayName => "Fake";
        public Type SetupComponent => typeof(object);
        public bool IsReady => true;
        public string? AccountName => "me@example.com";
        public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        {
            Fetches++;
            return Task.FromResult<IReadOnlyList<CalendarEvent>>([.. Events]);
        }
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 50, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
    private readonly ManualTime _time = new(T0);
    private readonly FakeDante _buddy = new();
    private readonly FakeCalendar _calendar = new();
    private readonly RecordingOpener _opener = new();
    private readonly ConfigStore _store;
    private readonly DeviceLink _link;
    private readonly DeviceAlerts _alerts;
    private readonly CalendarService _service;

    public CalendarServiceTests()
    {
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
        var cfg = _store.Snapshot();
        cfg.SetFeature(CalendarSettings.Key, new CalendarSettings { Enabled = true, Provider = "fake", ReminderMinutes = [10, 1] });
        _store.Save(cfg);
        _link = new DeviceLink(_store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => _buddy);
        _alerts = new DeviceAlerts(_link);
        _service = new CalendarService(_store, [_calendar], _alerts, _opener, _time, NullLogger<CalendarService>.Instance);
        _service.Initialize(engine: null);   // no engine: the "next meeting" screen isn't part of these tests
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Syncs_and_nudges_before_the_meeting_and_SELECT_opens_the_join_link()
    {
        _calendar.Events.Add(new CalendarEvent("e1", "Standup", T0.AddMinutes(10), T0.AddMinutes(25), JoinUrl: "https://teams.microsoft.com/l/meetup-join/x"));
        _calendar.Events.Add(new CalendarEvent("e2", "Lunch", T0.AddMinutes(70), T0.AddMinutes(130), ShowAs: ShowAs.Free));

        await _service.StepAsync(default);
        Assert.Equal(1, _calendar.Fetches);
        Assert.Equal("Standup", Assert.Single(_service.Upcoming).Title);   // "free" lunch is filtered out
        var alert = Assert.Single(_alerts.Active);
        Assert.Equal(("calendar", "Standup in 10 min", 1), (alert.Id, alert.Text, alert.Level));

        _time.Now = T0.AddMinutes(1);
        await _service.StepAsync(default);
        Assert.Equal(1, _calendar.Fetches);   // syncs every 5 minutes, not every step

        // "I'm on it" on DANTE: the Teams link opens and the 1-minute reminder never comes.
        Assert.True(_alerts.TryAnswer(new DeviceEvent("ack", (JsonObject)JsonNode.Parse("""{"t":"ack","id":"calendar"}""")!)));
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/x", Assert.Single(_opener.Opened));
        _time.Now = T0.AddMinutes(9).AddSeconds(30);
        await _service.StepAsync(default);
        Assert.Empty(_alerts.Active);
    }

    [Fact]
    public async Task Snooze_waits_for_the_next_reminder_which_is_urgent_and_the_nudge_goes_once_it_started()
    {
        _calendar.Events.Add(new CalendarEvent("e1", "1:1", T0.AddMinutes(10), T0.AddMinutes(40)));
        await _service.StepAsync(default);
        _alerts.TryAnswer(new DeviceEvent("snooze", (JsonObject)JsonNode.Parse("""{"t":"snooze","id":"calendar"}""")!));
        Assert.Empty(_alerts.Active);

        _time.Now = T0.AddMinutes(9);
        await _service.StepAsync(default);
        var last = Assert.Single(_alerts.Active);
        Assert.Equal(("1:1 in 1 min", 2), (last.Text, last.Level));   // the last reminder is urgent

        _time.Now = T0.AddMinutes(16);   // the meeting has been going for 6 minutes
        await _service.StepAsync(default);
        Assert.Empty(_alerts.Active);
        Assert.Empty(_opener.Opened);
    }

    [Fact]
    public async Task Turned_off_means_no_reminders()
    {
        var cfg = _store.Snapshot();
        cfg.SetFeature(CalendarSettings.Key, new CalendarSettings { Enabled = false, Provider = "fake" });
        _store.Save(cfg);
        _calendar.Events.Add(new CalendarEvent("e1", "Standup", T0.AddMinutes(1), T0.AddMinutes(15)));
        await _service.StepAsync(default);
        Assert.Empty(_alerts.Active);
        Assert.Equal(0, _calendar.Fetches);
    }
}
