using System.Diagnostics;
using Hank.Core.Actions;
using Hank.Core.Behavior;
using Hank.Core.Config;
using Hank.Core.Device;
using Hank.Core.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hank.Core.Engine;

public interface IUrlOpener
{
    void Open(string url);
}

public sealed class ShellUrlOpener : IUrlOpener
{
    public void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}

/// <summary>What the UI shows per app.</summary>
public sealed record AppStatus(
    AppDefinition Definition,
    SourceReading? Reading,
    int Level,
    bool AlertRaised,
    DateTimeOffset? UpdatedAt,
    string? Error);

/// <summary>
/// Polls every app's data source, turns readings into levels/quips/nudges, works out HANK's
/// mood and pushes the results to the device. All "thinking" happens here, on the PC.
/// </summary>
public sealed class HankEngine : BackgroundService
{
    private sealed class Runtime(AppDefinition def)
    {
        public AppDefinition Def = def;
        public readonly TakeoverTracker Takeover = new();
        public SourceReading? Reading;
        public int Level;
        public DateTimeOffset NextPoll = DateTimeOffset.MinValue;
        public DateTimeOffset? UpdatedAt;
        public DateTimeOffset? NonZeroSince;
        public bool Polling;
        public string? AlertText;
        public string? AlertKey;
        public string? Error;
    }

    private readonly ConfigStore _config;
    private readonly SourceRegistry _sources;
    private readonly DeviceLink _link;
    private readonly IUrlOpener _urls;
    private readonly TimeProvider _time;
    private readonly ILogger<HankEngine> _log;
    private readonly IPcActions _actions;
    private readonly Random _rng = new();
    private readonly Lock _gate = new();

    private HankConfig _cfg;
    private List<Runtime> _apps = [];
    private MoodResult _mood = new(Mood.Happy, 0, TimeSpan.Zero);
    private MoodResult? _sentMood;
    private (Mood Mood, DateTimeOffset Until)? _moodOverride;

    public HankEngine(ConfigStore config, SourceRegistry sources, DeviceLink link, IUrlOpener urls,
        TimeProvider time, ILogger<HankEngine> log, PushStore push, ClaudeCodeSessionTracker claude, IPcActions actions)
    {
        _actions = actions;
        _config = config;
        _sources = sources;
        _link = link;
        _urls = urls;
        _time = time;
        _log = log;
        _cfg = config.Snapshot();
        Rebuild(_cfg);

        config.Changed += OnConfigChanged;
        link.EventReceived += OnDeviceEvent;
        push.Pushed += PollNow;
        claude.Changed += () => PollSourceType(ClaudeCodeWaitingSource.TypeName);
    }

    public event Action? StateChanged;

    public MoodResult CurrentMood
    {
        get { lock (_gate) return EffectiveMood(_time.GetUtcNow()); }
    }

    public IReadOnlyList<AppStatus> Snapshot()
    {
        lock (_gate)
            return [.. _apps.Select(r => new AppStatus(r.Def, r.Reading, r.Level, r.Takeover.Raised, r.UpdatedAt, r.Error))];
    }

    /// <summary>Poll an app right away (pushes, hooks, the "refresh" button).</summary>
    public void PollNow(string appId)
    {
        lock (_gate)
            foreach (var r in _apps.Where(r => r.Def.Id.Equals(appId, StringComparison.OrdinalIgnoreCase)))
                r.NextPoll = DateTimeOffset.MinValue;
    }

    public void PollAll()
    {
        lock (_gate)
            foreach (var r in _apps) r.NextPoll = DateTimeOffset.MinValue;
    }

    public void Say(string text) => _link.Send(DeviceProtocol.Say(text));

    public void TestAlert(string text, int level) =>
        _link.Send(DeviceProtocol.Alert("test", "HANK", text, level));

    /// <summary>Runs the MAGIC button's action (also used by the "Try it" button in the UI).</summary>
    /// <returns>What happened, e.g. "Mic muted" (also shown on HANK if configured).</returns>
    public async Task<string> RunMagicAsync(bool longPress, CancellationToken ct = default)
    {
        MagicAction action;
        lock (_gate) action = longPress ? _cfg.Magic.LongPress : _cfg.Magic.Press;
        if (action.Kind == MagicActionKind.Nothing) return "";

        string message;
        try
        {
            if (action.Kind == MagicActionKind.RefreshApps)
            {
                PollAll();
                message = "Refreshing apps!";
            }
            else
            {
                message = await _actions.RunAsync(action, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Magic button action {Action} failed", action.Kind);
            message = ex is ActionFailedException or PlatformNotSupportedException ? ex.Message : $"Magic failed: {ex.Message}";
            _link.Send(DeviceProtocol.Say(message));   // always tell the human when it didn't work
            return message;
        }
        if (action.ConfirmOnBuddy && !string.IsNullOrWhiteSpace(message)) _link.Send(DeviceProtocol.Say(message));
        return message;
    }

    /// <summary>Lets the config UI preview a mood on the device for a little while.</summary>
    public void PreviewMood(Mood mood, TimeSpan duration)
    {
        lock (_gate) _moodOverride = (mood, _time.GetUtcNow() + duration);
    }

    /// <summary>One-off reading for the "Test" button in the app editor (doesn't touch the device).</summary>
    public async Task<SourceReading> TestAsync(AppDefinition app, CancellationToken ct)
    {
        var source = _sources.Find(app.SourceType);
        if (source is null) return SourceReading.Fail($"Unknown source '{app.SourceType}'.");
        try { return await source.ReadAsync(app, ct); }
        catch (Exception ex) { return SourceReading.Fail(ex.Message); }
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
        SourceReading reading;
        var source = _sources.Find(runtime.Def.SourceType);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            reading = source is null
                ? SourceReading.Fail($"Unknown source '{runtime.Def.SourceType}'.")
                : await source.ReadAsync(runtime.Def, timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            reading = SourceReading.Fail(ex.Message);
        }

        lock (_gate)
        {
            runtime.Polling = false;
            if (!_apps.Contains(runtime)) return; // app removed while polling
            Apply(runtime, reading, _time.GetUtcNow());
        }
        StateChanged?.Invoke();
    }

    /// <summary>Folds a new reading into the app's state and tells HANK (with a quip if it's worth one).</summary>
    private void Apply(Runtime r, SourceReading reading, DateTimeOffset now)
    {
        if (reading.Error is not null)
        {
            // Keep showing the last good value; just remember the error for the UI.
            r.Error = reading.Error;
            return;
        }

        var old = r.Reading;
        var level = LevelEvaluator.Evaluate(r.Def.Levels, reading.Value);
        r.Error = null;
        r.Reading = reading;
        r.UpdatedAt = now;
        r.NonZeroSince = reading.Value > 0 ? r.NonZeroSince ?? now : null;

        var changed = old is null || old.Value != reading.Value || old.Text != reading.Text || r.Level != level;
        var quip = old is null ? null
            : QuipBank.Pick(_cfg.Personality, new QuipContext(r.Def, old.Value, reading.Value, r.Level, level, reading.Text), _rng);
        r.Level = level;

        if (changed) SendValue(r, quip);
        EvaluateTakeover(r, now);
    }

    private void SendValue(Runtime r, string? quip)
    {
        if (!r.Def.ShowInCarousel && quip is null) return;
        if (r.Def.ShowInCarousel)
            _link.Send(DeviceProtocol.Value(r.Def.Id, r.Reading?.Value, r.Reading?.Text, r.Level, quip));
        else
            _link.Send(DeviceProtocol.Say(quip!));
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

    private void OnConfigChanged(HankConfig cfg)
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
    private void Rebuild(HankConfig cfg)
    {
        var old = _apps.ToDictionary(r => r.Def.Id, StringComparer.OrdinalIgnoreCase);
        var next = new List<Runtime>();
        foreach (var def in cfg.Apps.Where(a => a.Enabled))
        {
            if (old.Remove(def.Id, out var existing) && existing.Def.SourceType == def.SourceType
                && existing.Def.SourceSettings.OrderBy(k => k.Key).SequenceEqual(def.SourceSettings.OrderBy(k => k.Key)))
            {
                existing.Def = def;
                existing.Level = LevelEvaluator.Evaluate(def.Levels, existing.Reading?.Value);
                next.Add(existing);
            }
            else
            {
                next.Add(new Runtime(def));
            }
        }
        // Anything removed must not stay on HANK's screen as a takeover.
        foreach (var gone in old.Values.Where(r => r.Takeover.Raised))
            _link.Send(DeviceProtocol.Clear(gone.Def.Id));
        _apps = next;
        _sentMood = null;
    }

    private void SendEverything(bool greeting)
    {
        _link.Send(DeviceProtocol.Cfg(_cfg.Device));
        _link.Send(DeviceProtocol.Apps(_apps.Where(r => r.Def.ShowInCarousel).Select(r => r.Def)));
        foreach (var r in _apps.Where(r => r.Def.ShowInCarousel && r.Reading is not null))
            _link.Send(DeviceProtocol.Value(r.Def.Id, r.Reading!.Value, r.Reading.Text, r.Level, null));
        foreach (var r in _apps.Where(r => r.Takeover.Raised))
            _link.Send(DeviceProtocol.Alert(r.Def.Id, r.Def.Name, r.AlertText ?? r.Def.Name, r.Def.Takeover.Level));
        _sentMood = null; // forces a mood message on the next tick
        if (greeting) _link.Send(DeviceProtocol.Say(QuipBank.Greeting(_cfg.Personality, _rng)));
    }

    private void OnDeviceEvent(DeviceEvent evt)
    {
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
                case "ack" when runtime is not null:
                    runtime.Takeover.Ack(now);
                    break;
                case "snooze" when runtime is not null:
                    runtime.Takeover.Snooze(runtime.Def.Takeover, now);
                    break;
                case "btn" when evt.Button == "magic":
                    _ = RunMagicAsync(evt.Long);   // never block the device link on a slow action
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

    private void PollSourceType(string type)
    {
        lock (_gate)
            foreach (var r in _apps.Where(r => r.Def.SourceType == type)) r.NextPoll = DateTimeOffset.MinValue;
    }
}
