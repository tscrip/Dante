using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Dante.Core;
using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.ClaudeCode;

/// <summary>
/// Fed by Claude Code hooks (POST /api/hooks/claude-code). Tracks which sessions are
/// currently waiting on the human and since when.
/// <list type="bullet">
/// <item><c>Notification</c> – Claude needs permission or has been idle waiting for input → waiting.</item>
/// <item><c>Stop</c> – Claude finished its turn → waiting for your next prompt (optional).</item>
/// <item><c>UserPromptSubmit</c>, <c>PostToolUse</c>, <c>SessionStart</c> – you're back / it's working → not waiting.</item>
/// <item><c>SessionEnd</c> – forget the session.</item>
/// </list>
/// </summary>
public sealed class ClaudeCodeSessionTracker(TimeProvider time)
{
    public sealed record WaitingSession(string SessionId, string Project, string Reason, DateTimeOffset Since);

    private readonly ConcurrentDictionary<string, WaitingSession> _waiting = new();

    public event Action? Changed;

    public DateTimeOffset? LastEventAt { get; private set; }
    public string? LastEventName { get; private set; }

    public IReadOnlyList<WaitingSession> Waiting => [.. _waiting.Values.OrderBy(w => w.Since)];

    public void Handle(JsonObject payload)
    {
        var evt = payload["hook_event_name"]?.GetValue<string>() ?? "";
        var session = payload["session_id"]?.GetValue<string>() ?? "default";
        var cwd = payload["cwd"]?.GetValue<string>() ?? "";
        var project = string.IsNullOrEmpty(cwd)
            ? "Claude Code"
            : Path.GetFileName(cwd.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : cwd;

        var now = time.GetUtcNow();
        LastEventAt = now;
        LastEventName = evt;

        var changed = evt switch
        {
            "Notification" => MarkWaiting(session, project, payload["message"]?.GetValue<string>() ?? "needs your input", now),
            "Stop" => MarkWaiting(session, project, "finished - your turn", now),
            "UserPromptSubmit" or "PostToolUse" or "SessionStart" or "SessionEnd" => _waiting.TryRemove(session, out _),
            _ => false,
        };
        if (changed) Changed?.Invoke();
    }

    public void Clear()
    {
        _waiting.Clear();
        Changed?.Invoke();
    }

    /// <summary>Keeps the original "since" if the session was already waiting, so the age keeps growing.</summary>
    private bool MarkWaiting(string session, string project, string reason, DateTimeOffset now)
    {
        _waiting.AddOrUpdate(session,
            _ => new WaitingSession(session, project, reason, now),
            (_, existing) => existing with { Reason = reason });
        return true;
    }
}

public sealed class ClaudeCodeWaitingSource(ClaudeCodeSessionTracker tracker, TimeProvider time) : IWidgetType
{
    public const string TypeName = "claude-code-waiting";

    public string Type => TypeName;
    public string PluginId => ClaudeCodePlugin.PluginId;
    public string DisplayName => "Sessions waiting on me";
    public string Description =>
        "Counts Claude Code sessions that are waiting for your feedback. Fed instantly by Claude Code hooks " +
        "(install them on this page).";
    public bool IsPushBased => true;
    public bool AllowMultiple => false;

    public WidgetDefinition CreateDefault() => new()
    {
        Name = "Claude Code",
        Type = TypeName,
        Visual = VisualKind.Status,
        Min = 0, Max = 5,
        PollSeconds = 5,
        CountsTowardMood = true,
        Levels = new LevelRule { WarnAt = 1, CritAt = 3 },
        Takeover = new TakeoverRule
        {
            Mode = TakeoverMode.WhenPending,
            DelaySeconds = 20,
            RenudgeMinutes = 5,
            SnoozeMinutes = 5,
            Level = 1,
            Message = "{item} is waiting on you!",
        },
    };

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("includeFinished", "Count 'finished, your turn' as waiting", SettingKind.Bool, "true",
            "Off = only count permission prompts / explicit questions."),
    ];

    public Task<WidgetReading> ReadAsync(WidgetDefinition app, CancellationToken ct)
    {
        var includeFinished = !string.Equals(app.Get(this, "includeFinished"), "false", StringComparison.OrdinalIgnoreCase);
        var waiting = tracker.Waiting
            .Where(w => includeFinished || !w.Reason.StartsWith("finished", StringComparison.Ordinal))
            .ToList();

        var pending = waiting.Select(w => new PendingItem(w.SessionId, w.Project, w.Since)).ToList();
        string text = waiting.Count switch
        {
            0 => "all quiet",
            1 => $"{waiting[0].Project}: {waiting[0].Reason}",
            _ => $"{waiting.Count} sessions, oldest {Format.Age(time.GetUtcNow() - waiting[0].Since)}",
        };
        return Task.FromResult(new WidgetReading { Value = waiting.Count, Text = text, Pending = pending });
    }
}
