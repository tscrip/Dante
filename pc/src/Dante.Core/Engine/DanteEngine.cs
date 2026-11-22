using System.Diagnostics;
using Dante.Core.Behavior;
using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Widgets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dante.Core.Engine;

public interface IUrlOpener
{
    void Open(string url);
}

public sealed class ShellUrlOpener : IUrlOpener
{
    public void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}

/// <summary>What the UI shows per app.</summary>
public sealed record WidgetStatus(
    WidgetDefinition Definition,
    WidgetReading? Reading,
    int Level,
    bool AlertRaised,
    DateTimeOffset? UpdatedAt,
    string? Error);

/// <summary>
/// Polls every app's data source, turns readings into levels/quips/nudges, works out DANTE's
/// mood and pushes the results to the device. All "thinking" happens here, on the PC.
/// Feature slices plug in through data sources (<see cref="IWidgetType"/>), button handlers
/// (<see cref="AddHandler"/>) and their own nudges (<see cref="DeviceAlerts"/>).
/// </summary>
public sealed class DanteEngine : BackgroundService
{
    private sealed class Runtime(WidgetDefinition def)
    {
        public WidgetDefinition Def = def;
        public readonly TakeoverTracker Takeover = new();
        public WidgetReading? Reading;
        public int Level;
        public DateTimeOffset NextPoll = DateTimeOffset.MinValue;
        public DateTimeOffset? UpdatedAt;
        public DateTimeOffset? NonZeroSince;
        public bool Polling;
        public string? AlertText;
        public string? AlertKey;
        public string? Error;
        /// <summary>Hash of the picture DANTE has for this widget (album art), so it is only sent when it changes.</summary>
        public string? SentArt;
    }

    private readonly ConfigStore _config;
    private readonly WidgetTypeRegistry _sources;
    private readonly DeviceLink _link;
    private readonly IUrlOpener _urls;
    private readonly TimeProvider _time;
    private readonly ILogger<DanteEngine> _log;
    private readonly DeviceAlerts _alerts;
    private readonly Random _rng = new();
    private readonly Lock _gate = new();
    private volatile (IDeviceEventHandler Handler, int Order)[] _handlers = [];

    private DanteConfig _cfg;
    private List<Runtime> _apps = [];
    private MoodResult _mood = new(Mood.Happy, 0, TimeSpan.Zero);
    private MoodResult? _sentMood;
    private (Mood Mood, DateTimeOffset Until)? _moodOverride;

    public DanteEngine(ConfigStore config, WidgetTypeRegistry sources, DeviceLink link, IUrlOpener urls,
        TimeProvider time, ILogger<DanteEngine> log, DeviceAlerts alerts)
    {
        _config = config;
        _sources = sources;
        _link = link;
        _urls = urls;
        _time = time;
        _log = log;
        _alerts = alerts;
        _cfg = config.Snapshot();
        Rebuild(_cfg);

        config.Changed += OnConfigChanged;
        link.EventReceived += OnDeviceEvent;
        alerts.Changed += () => StateChanged?.Invoke();
    }

    public event Action? StateChanged;

    /// <summary>
    /// Lets a feature react to DANTE's buttons before the engine does (see <see cref="IDeviceEventHandler"/>).
    /// Features register their handlers when the app starts.
    /// </summary>
    /// <param name="order">Lower runs first. Handlers for a specific screen (e.g. a timer that SELECT starts) should
    /// come before catch-all ones (the MAGIC button's PC action).</param>
    public void AddHandler(IDeviceEventHandler handler, int order = 0) =>
        _handlers = [.. _handlers.Append((Handler: handler, Order: order)).OrderBy(h => h.Order)];

    public MoodResult CurrentMood
    {
        get { lock (_gate) return EffectiveMood(_time.GetUtcNow()); }
    }

    public IReadOnlyList<WidgetStatus> Snapshot()
    {
        lock (_gate)
            return [.. _apps.Select(r => new WidgetStatus(r.Def, r.Reading, r.Level, r.Takeover.Raised, r.UpdatedAt, r.Error))];
    }

    /// <summary>Poll an app right away (pushes, hooks, the "refresh" button).</summary>
    public void PollNow(string appId)
    {
        lock (_gate)
            foreach (var r in _apps.Where(r => r.Def.Id.Equals(appId, StringComparison.OrdinalIgnoreCase)))
                r.NextPoll = DateTimeOffset.MinValue;
    }

    /// <summary>Poll every app fed by this kind of source right away (e.g. when the source's data changed).</summary>
    public void PollWidgetType(string type)
    {
        lock (_gate)
            foreach (var r in _apps.Where(r => r.Def.Type.Equals(type, StringComparison.OrdinalIgnoreCase)))
                r.NextPoll = DateTimeOffset.MinValue;
    }

    public void PollAll()
    {
        lock (_gate)
            foreach (var r in _apps) r.NextPoll = DateTimeOffset.MinValue;
    }

    public void Say(string text) => _link.Send(DeviceProtocol.Say(text));

    public void TestAlert(string text, int level) =>
        _link.Send(DeviceProtocol.Alert("test", "DANTE", text, level));

    /// <summary>Lets the config UI preview a mood on the device for a little while.</summary>
    public void PreviewMood(Mood mood, TimeSpan duration)
    {
        lock (_gate) _moodOverride = (mood, _time.GetUtcNow() + duration);
    }

    /// <summary>One-off reading for the "Test" button in the app editor (doesn't touch the device).</summary>
    public async Task<WidgetReading> TestAsync(WidgetDefinition app, CancellationToken ct)
    {
        var source = _sources.Find(app.Type);
        if (source is null) return WidgetReading.Fail($"Unknown source '{app.Type}'.");
        try { return await source.ReadAsync(app, ct); }
        catch (Exception ex) { return WidgetReading.Fail(ex.Message); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { Tick(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Engine tick failed"); }
        }
    }

    /// <summary>Starts due polls, then re-evaluates time-based things (nudge delays, mood ageing).</summary>
    internal void Tick(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        List<Runtime> due;
        lock (_gate)
        {
            due = [.. _apps.Where(r => !r.Polling && now >= r.NextPoll)];
            foreach (var r in due)
            {
                r.Polling = true;
                r.NextPoll = now + TimeSpan.FromSeconds(r.Def.PollSeconds);
            }
        }
        foreach (var r in due) _ = PollAsync(r, ct);

        bool changed;
        lock (_gate) changed = EvaluateTakeovers(now) | EvaluateMood(now);
        if (changed) StateChanged?.Invoke();
    }

    private async Task PollAsync(Runtime runtime, CancellationToken ct)
    {
        WidgetReading reading;
        var source = _sources.Find(runtime.Def.Type);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            reading = source is null
                ? WidgetReading.Fail($"Unknown source '{runtime.Def.Type}'.")
                : await source.ReadAsync(runtime.Def, timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            reading = WidgetReading.Fail(ex.Message);
        }

        lock (_gate)
        {
            runtime.Polling = false;
            if (!_apps.Contains(runtime)) return; // app removed while polling
            Apply(runtime, reading, _time.GetUtcNow());
        }
        StateChanged?.Invoke();
    }

    /// <summary>Folds a new reading into the app's state and tells DANTE (with a quip if it's worth one).</summary>
    private void Apply(Runtime r, WidgetReading reading, DateTimeOffset now)
    {
        if (reading.Error is not null)
        {
            // Keep showing the last good value; just remember the error for the UI.
            r.Error = reading.Error;
            return;
        }

        var old = r.Reading;
        var level = LevelEvaluator.Evaluate(r.Def.Levels, reading.LevelValue);
        r.Error = null;
        r.Reading = reading;
        r.UpdatedAt = now;
        r.NonZeroSince = reading.Value > 0 ? r.NonZeroSince ?? now : null;

        var changed = old is null || old.Value != reading.Value || old.Value2 != reading.Value2 || old.Text != reading.Text || r.Level != level || old.Timer != reading.Timer
                      || !SameMedia(old.Media, reading.Media);
        // A running countdown changes every poll, and a media player every song: neither is news worth a quip.
        var quip = old is null || reading.Timer is not null || reading.Media is not null ? null
            : QuipBank.Pick(_cfg.Personality, new QuipContext(r.Def, old.Value, reading.Value, r.Level, level, reading.Text), _rng);
        r.Level = level;

        if (changed) SendValue(r, quip);
        EvaluateTakeover(r, now);
    }

    /// <summary>Same media state (the album art compared by content: a player may hand over a new copy each time).</summary>
    private static bool SameMedia(MediaReading? a, MediaReading? b) =>
        a is null || b is null ? a is null && b is null
        : a.Artist == b.Artist && a.Playing == b.Playing && a.Muted == b.Muted && a.Volume == b.Volume
          && (a.Art ?? []).AsSpan().SequenceEqual(b.Art ?? []);

    private void SendValue(Runtime r, string? quip)
    {
        if (!r.Def.ShowInCarousel && quip is null) return;
        if (r.Def.ShowInCarousel)
        {
            SendArtIfChanged(r);
            _link.Send(DeviceProtocol.Value(r.Def.Id, r.Reading, r.Level, quip));
        }
        else
            _link.Send(DeviceProtocol.Say(quip!));
    }

    /// <summary>Album art goes to DANTE (in chunks) only when the picture changed, before the value that shows it.</summary>
    private void SendArtIfChanged(Runtime r)
    {
        if (r.Reading?.Media?.Art is not { } art || !DeviceProtocol.ArtFits(art)) return;
        var hash = DeviceProtocol.ArtHash(art);
        if (hash == r.SentArt) return;
        foreach (var chunk in DeviceProtocol.Art(r.Def.Id, art)) _link.Send(chunk);
        r.SentArt = hash;
    }

    private bool EvaluateTakeovers(DateTimeOffset now)
    {
        var any = false;
        foreach (var r in _apps) any |= EvaluateTakeover(r, now);
        return any;
    }

    private bool EvaluateTakeover(Runtime r, DateTimeOffset now)
    {
        var rule = r.Def.Takeover;
        var condition = r.Reading is not null && TakeoverTracker.ConditionHolds(rule, r.Reading);
        switch (r.Takeover.Evaluate(rule, condition, now))
        {
            case TakeoverAction.Raise:
                r.AlertKey = AlertKey(r);
                r.AlertText = AlertText(r, now);
                _link.Send(DeviceProtocol.Alert(r.Def.Id, r.Def.Name, r.AlertText, rule.Level));
                return true;
            case TakeoverAction.Clear:
                r.AlertText = null;
                _link.Send(DeviceProtocol.Clear(r.Def.Id));
                return true;
            default:
                // Still raised but the substance changed (e.g. a second session started waiting): refresh it.
                if (r.Takeover.Raised && AlertKey(r) != r.AlertKey)
                {
                    r.AlertKey = AlertKey(r);
                    r.AlertText = AlertText(r, now);
                    _link.Send(DeviceProtocol.Alert(r.Def.Id, r.Def.Name, r.AlertText, rule.Level));
                }
                return false;
        }
    }

    /// <summary>What the nudge is about, ignoring the ever-growing age so we don't re-send every minute.</summary>
    private static string AlertKey(Runtime r) =>
        $"{r.Reading?.Value}|{r.Reading?.Pending?.Count}|{r.Reading?.Pending?.OrderBy(p => p.Since).FirstOrDefault()?.Key}";

    private static string AlertText(Runtime r, DateTimeOffset now)
    {
        var reading = r.Reading;
        var pending = reading?.Pending ?? [];
        var oldest = pending.Count > 0 ? pending.Min(p => p.Since) : (DateTimeOffset?)null;
        var tokens = new Dictionary<string, string>
        {
            ["name"] = r.Def.Name,
            ["value"] = Format.Number(reading?.Value),
            ["unit"] = r.Def.Unit,
            ["text"] = reading?.Text ?? "",
            ["count"] = pending.Count > 0 ? pending.Count.ToString() : Format.Number(reading?.Value),
            ["item"] = pending.OrderBy(p => p.Since).FirstOrDefault()?.Title ?? r.Def.Name,
            ["oldest"] = oldest is { } o ? Format.Age(now - o) : "",
        };
        var template = string.IsNullOrWhiteSpace(r.Def.Takeover.Message)
            ? (pending.Count > 1 ? "{count} things need you! Oldest: {item}" : "{name}: {text}")
            : r.Def.Takeover.Message;
        return Templates.Render(template, tokens);
    }

    private bool EvaluateMood(DateTimeOffset now)
    {
        var pending = new List<PendingItem>();
        foreach (var r in _apps.Where(r => r.Def.CountsTowardMood && r.Reading is not null))
        {
            if (r.Reading!.Pending is { } items)
                pending.AddRange(items.Select(i => i with { Key = r.Def.Id + ":" + i.Key }));
            else if (r.Reading.Value is > 0 and var v && r.NonZeroSince is { } since)
                pending.AddRange(Enumerable.Range(0, (int)Math.Min(v, 100)).Select(i => new PendingItem($"{r.Def.Id}:{i}", r.Def.Name, since)));
        }
        _mood = MoodCalculator.Evaluate(_cfg.Mood, pending, now);

        var effective = EffectiveMood(now);
        // Re-send when the mood changes, the count changes, or the age ticks over another 5 minutes.
        if (_sentMood is { } sent && sent.Mood == effective.Mood && sent.PendingCount == effective.PendingCount
            && (int)sent.OldestWait.TotalMinutes / 5 == (int)effective.OldestWait.TotalMinutes / 5)
            return false;

        _sentMood = effective;
        _link.Send(DeviceProtocol.Mood(effective));
        return true;
    }

    private MoodResult EffectiveMood(DateTimeOffset now)
    {
        if (_moodOverride is { } o && o.Until > now) return _mood with { Mood = o.Mood };
        _moodOverride = null;
        return _mood;
    }

    private void OnConfigChanged(DanteConfig cfg)
    {
        lock (_gate)
        {
            _cfg = cfg;
            Rebuild(cfg);
            SendEverything(greeting: false);
        }
        StateChanged?.Invoke();
    }

    /// <summary>Keeps state for apps whose source didn't change; new/changed apps poll immediately.</summary>
    private void Rebuild(DanteConfig cfg)
    {
        var old = _apps.ToDictionary(r => r.Def.Id, StringComparer.OrdinalIgnoreCase);
        var next = new List<Runtime>();
        foreach (var def in cfg.Widgets.Where(a => a.Active))
        {
            if (old.Remove(def.Id, out var existing) && existing.Def.Type == def.Type
                && existing.Def.Settings.OrderBy(k => k.Key).SequenceEqual(def.Settings.OrderBy(k => k.Key)))
            {
                existing.Def = def;
                existing.Level = LevelEvaluator.Evaluate(def.Levels, existing.Reading?.LevelValue);
                next.Add(existing);
            }
            else
            {
                next.Add(new Runtime(def));
            }
        }
        // Anything removed must not stay on DANTE's screen as a takeover.
        foreach (var gone in old.Values.Where(r => r.Takeover.Raised))
            _link.Send(DeviceProtocol.Clear(gone.Def.Id));
        _apps = next;
        _sentMood = null;
    }

    private void SendEverything(bool greeting)
    {
        _link.Send(DeviceProtocol.Cfg(_cfg.Device));
        _link.Send(DeviceProtocol.Widgets(_apps.Where(r => r.Def.ShowInCarousel).Select(r => r.Def)));
        foreach (var r in _apps.Where(r => r.Def.ShowInCarousel && r.Reading is not null))
        {
            r.SentArt = null;   // DANTE may have restarted: its pictures are gone
            SendArtIfChanged(r);
            _link.Send(DeviceProtocol.Value(r.Def.Id, r.Reading, r.Level, null));
        }
        foreach (var r in _apps.Where(r => r.Takeover.Raised))
            _link.Send(DeviceProtocol.Alert(r.Def.Id, r.Def.Name, r.AlertText ?? r.Def.Name, r.Def.Takeover.Level));
        _alerts.ResendAll();
        _sentMood = null; // forces a mood message on the next tick
        if (greeting) _link.Send(DeviceProtocol.Say(QuipBank.Greeting(_cfg.Personality, _rng)));
        // Countdowns must be re-read: the device's clock restarted with the link.
        foreach (var r in _apps.Where(r => r.Reading?.Timer is not null)) r.NextPoll = DateTimeOffset.MinValue;
    }

    private void OnDeviceEvent(DeviceEvent evt)
    {
        // Feature nudges answered on the device belong to the feature that raised them.
        if (_alerts.TryAnswer(evt)) return;

        WidgetDefinition? screen;
        lock (_gate)
        {
            var id = evt.Id ?? evt.Screen;
            screen = _apps.FirstOrDefault(r => r.Def.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Def;
        }
        // Feature slices first (outside the lock: they may call back into the engine).
        foreach (var (handler, _) in _handlers)
        {
            try
            {
                if (handler.Handle(evt, screen)) { StateChanged?.Invoke(); return; }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Device event handler {Handler} failed", handler.GetType().Name);
            }
        }

        var now = _time.GetUtcNow();
        lock (_gate)
        {
            var app = evt.Id ?? evt.Screen;
            var runtime = _apps.FirstOrDefault(r => r.Def.Id.Equals(app, StringComparison.OrdinalIgnoreCase));
            switch (evt.Type)
            {
                case "hello":
                    SendEverything(greeting: true);
                    break;
                case "sync":   // DANTE noticed it missed something (e.g. the widget list): resend, quietly
                    SendEverything(greeting: false);
                    break;
                case "ack" when runtime is not null:
                    runtime.Takeover.Ack(now);
                    break;
                case "snooze" when runtime is not null:
                    runtime.Takeover.Snooze(runtime.Def.Takeover, now);
                    break;
                case "btn" when evt.Button == "sel" && evt.Long:
                    foreach (var r in _apps) r.NextPoll = DateTimeOffset.MinValue;
                    _link.Send(DeviceProtocol.Say("Refreshing everything!"));
                    break;
                case "btn" when evt.Button == "sel" && runtime is not null:
                    OpenFor(runtime);
                    break;
            }
        }
        StateChanged?.Invoke();
    }

    private void OpenFor(Runtime r)
    {
        var url = r.Def.OnSelectUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        var firstItemUrl = r.Reading?.Pending?.OrderBy(p => p.Since).FirstOrDefault(p => p.Url is not null)?.Url;
        url = url.Replace("{url}", firstItemUrl ?? "", StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return;
        try
        {
            _urls.Open(uri.ToString());
            _link.Send(DeviceProtocol.Say($"Opening {r.Def.Name}..."));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not open {Url}", uri);
        }
    }
}
