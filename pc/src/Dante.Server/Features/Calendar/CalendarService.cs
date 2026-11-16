using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;

namespace Dante.Server.Features.Calendar;

/// <summary>
/// Keeps a copy of the next hours of the calendar and nudges on DANTE before each meeting, at the
/// minutes set on the Calendar page. SELECT on the nudge ("I'm on it") opens the meeting's join link;
/// LEFT/RIGHT ("later") waits for the next reminder.
/// </summary>
public sealed class CalendarService(ConfigStore config, IEnumerable<ICalendarProvider> providers, DeviceAlerts alerts,
    IUrlOpener urls, TimeProvider time, ILogger<CalendarService> log) : BackgroundService
{
    public const string AlertId = "calendar";
    private static readonly TimeSpan LookBack = TimeSpan.FromHours(1);
    private static readonly TimeSpan LookAhead = TimeSpan.FromHours(12);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly HashSet<string> _fired = [];
    private readonly HashSet<string> _dismissed = [];
    private IReadOnlyList<CalendarEvent> _events = [];
    private DueReminder? _showing;
    private DateTimeOffset _nextSync = DateTimeOffset.MinValue;
    private DanteEngine? _engine;

    public IReadOnlyList<ICalendarProvider> Providers { get; } = [.. providers];
    public DateTimeOffset? LastSync { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Events were fetched, a reminder fired, or the settings changed.</summary>
    public event Action? Changed;

    public CalendarSettings Settings => config.Snapshot().Feature<CalendarSettings>(CalendarSettings.Key).Normalized();

    public ICalendarProvider? Provider => Providers.FirstOrDefault(p => p.Id == Settings.Provider) ?? Providers.FirstOrDefault();

    /// <summary>Meetings from now on that get reminders, soonest first.</summary>
    public IReadOnlyList<CalendarEvent> Upcoming
    {
        get
        {
            var now = time.GetUtcNow();
            var settings = Settings;
            lock (_gate) return [.. _events.Where(e => e.End > now && settings.Wants(e)).OrderBy(e => e.Start)];
        }
    }

    public CalendarEvent? Next
    {
        get
        {
            var now = time.GetUtcNow();
            return Upcoming.FirstOrDefault(e => e.Start + ReminderPlanner.Grace > now);
        }
    }

    public TimeZoneInfo LocalZone => time.LocalTimeZone;

    /// <summary>Hooks up the device answers and the engine (for the "next meeting" screen); called at start-up.</summary>
    public void Initialize(DanteEngine? engine)
    {
        _engine = engine;
        alerts.Acknowledged += a => { if (a.Id == AlertId) OnAnswered(acknowledged: true); };
        alerts.Snoozed += a => { if (a.Id == AlertId) OnAnswered(acknowledged: false); };
        config.Changed += _ => { _nextSync = DateTimeOffset.MinValue; Changed?.Invoke(); };
    }

    /// <summary>Fetch now (the "Sync now" button, or after signing in).</summary>
    public void SyncSoon() => _nextSync = DateTimeOffset.MinValue;

    /// <summary>Shows a sample reminder on DANTE.</summary>
    public void TestReminder()
    {
        var sample = Next ?? new CalendarEvent("test", "Team standup", time.GetUtcNow().AddMinutes(5), time.GetUtcNow().AddMinutes(20));
        var minutes = Math.Max(0, (int)Math.Ceiling((sample.Start - time.GetUtcNow()).TotalMinutes));
        var reminder = new DueReminder(sample, minutes, minutes, false);
        lock (_gate) _showing = reminder;
        alerts.Raise(AlertId, "Meeting", ReminderPlanner.Text(reminder, Settings, LocalZone), Settings.NudgeLevel);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var p in Providers.OfType<Microsoft365CalendarProvider>())
        {
            try { await p.RestoreAsync(stoppingToken); }
            catch (Exception ex) { log.LogWarning(ex, "Could not restore the calendar sign-in"); }
        }

        using var tick = new PeriodicTimer(CheckEvery, time);
        do
        {
            try { await StepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Calendar step failed"); }
        }
        while (await tick.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One round: sync if it's time, then fire due reminders and tidy up finished ones.</summary>
    internal async Task StepAsync(CancellationToken ct)
    {
        var settings = Settings;
        var now = time.GetUtcNow();
        if (!settings.Enabled)
        {
            alerts.Clear(AlertId);
            return;
        }

        if (now >= _nextSync && Provider is { IsReady: true } provider)
        {
            _nextSync = now + TimeSpan.FromMinutes(settings.SyncMinutes);
            try
            {
                var events = await provider.GetEventsAsync(now - LookBack, now + LookAhead, ct);
                lock (_gate) _events = events;
                LastSync = now;
                LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                LastError = ex.Message;
                log.LogWarning(ex, "Calendar sync failed");
                _nextSync = now + TimeSpan.FromMinutes(1);   // try again soon
            }
            _engine?.PollWidgetType(NextMeetingSource.TypeName);
            Changed?.Invoke();
        }

        IReadOnlyList<DueReminder> due;
        lock (_gate)
        {
            due = ReminderPlanner.Due(_events, settings, now, _fired, _dismissed);
            // The nudge for a meeting that is well under way can go.
            if (_showing is { } s && now >= s.Event.Start + ReminderPlanner.Grace) _showing = null;
        }
        if (due.Count > 0)
        {
            var first = due[0];   // the soonest meeting wins the screen
            lock (_gate) _showing = first;
            var level = settings.LastReminderUrgent && first.IsLast ? 2 : settings.NudgeLevel;
            var title = due.Count > 1 ? $"{due.Count} meetings" : "Meeting";
            alerts.Raise(AlertId, title, ReminderPlanner.Text(first, settings, LocalZone), level);
            _engine?.PollWidgetType(NextMeetingSource.TypeName);
            Changed?.Invoke();
        }
        else if (alerts.IsActive(AlertId) && _showing is null)
        {
            alerts.Clear(AlertId);
        }
    }

    private void OnAnswered(bool acknowledged)
    {
        DueReminder? answered;
        lock (_gate)
        {
            answered = _showing;
            _showing = null;
            // "I'm on it": no more reminders for this meeting. "Later": the next configured reminder still comes.
            if (acknowledged && answered is not null) _dismissed.Add(answered.Event.OccurrenceKey);
        }
        if (acknowledged && answered?.Event.JoinUrl is { } join && Settings.OpenJoinLinkOnSelect
            && Uri.TryCreate(join, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            try
            {
                urls.Open(uri.ToString());
                alerts.Say("Opening the meeting...");
            }
            catch (Exception ex) { log.LogWarning(ex, "Could not open the meeting link"); }
        }
        Changed?.Invoke();
    }
}
