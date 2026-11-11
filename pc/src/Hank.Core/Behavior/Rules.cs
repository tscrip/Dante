using Hank.Core.Config;
using Hank.Core.Sources;

namespace Hank.Core.Behavior;

public sealed record MoodResult(Mood Mood, int PendingCount, TimeSpan OldestWait);

public static class MoodCalculator
{
    /// <summary>
    /// HANK's mood depends on how many things are waiting on you and how long the oldest has waited.
    /// The most severe rule that fires wins; with nothing pending HANK is always happy.
    /// </summary>
    public static MoodResult Evaluate(MoodSettings settings, IReadOnlyCollection<PendingItem> pending, DateTimeOffset now)
    {
        if (pending.Count == 0) return new MoodResult(Mood.Happy, 0, TimeSpan.Zero);

        var oldest = now - pending.Min(p => p.Since);
        if (oldest < TimeSpan.Zero) oldest = TimeSpan.Zero;

        var mood = Mood.Happy;
        foreach (var rule in settings.Rules)
        {
            var byCount = rule.MinItems > 0 && pending.Count >= rule.MinItems;
            var byAge = rule.MinOldestMinutes > 0 && oldest.TotalMinutes >= rule.MinOldestMinutes;
            if ((byCount || byAge) && rule.Mood > mood) mood = rule.Mood;
        }
        return new MoodResult(mood, pending.Count, oldest);
    }
}

public static class LevelEvaluator
{
    /// <returns>0 ok, 1 warn, 2 crit.</returns>
    public static int Evaluate(LevelRule rule, double? value)
    {
        if (value is not { } v) return 0;
        bool Hit(double? threshold) => threshold is { } t && (rule.HigherIsWorse ? v >= t : v <= t);
        return Hit(rule.CritAt) ? 2 : Hit(rule.WarnAt) ? 1 : 0;
    }
}

public enum TakeoverAction { None, Raise, Clear }

/// <summary>
/// Per-app nudge state machine: condition must hold for <see cref="TakeoverRule.DelaySeconds"/>, then the
/// HANK takes over the screen. "I'm on it" (ack) silences it until it re-nudges or the condition resets;
/// "later" (snooze) silences it for <see cref="TakeoverRule.SnoozeMinutes"/>.
/// </summary>
public sealed class TakeoverTracker
{
    private DateTimeOffset? _conditionSince;
    private DateTimeOffset? _ackedAt;
    private DateTimeOffset? _snoozedUntil;

    public bool Raised { get; private set; }

    public static bool ConditionHolds(TakeoverRule rule, SourceReading? reading) => rule.Mode switch
    {
        TakeoverMode.WhenPending => (reading?.Pending?.Count ?? 0) > 0 || (reading?.Pending is null && reading?.Value > 0),
        TakeoverMode.WhenAbove => reading?.Value >= rule.Threshold,
        TakeoverMode.WhenBelow => reading?.Value <= rule.Threshold,
        _ => false,
    };

    public TakeoverAction Evaluate(TakeoverRule rule, bool condition, DateTimeOffset now)
    {
        if (!condition || rule.Mode == TakeoverMode.Never)
        {
            _conditionSince = null;
            _ackedAt = null;
            _snoozedUntil = null;
            return Lower();
        }

        _conditionSince ??= now;
        var snoozed = _snoozedUntil > now;
        var acked = _ackedAt is { } a && (rule.RenudgeMinutes <= 0 || now - a < TimeSpan.FromMinutes(rule.RenudgeMinutes));
        if (snoozed || acked) return Lower();

        if (!Raised && now - _conditionSince >= TimeSpan.FromSeconds(rule.DelaySeconds))
        {
            Raised = true;
            return TakeoverAction.Raise;
        }
        return TakeoverAction.None;
    }

    public void Ack(DateTimeOffset now)
    {
        _ackedAt = now;
        Raised = false;
    }

    public void Snooze(TakeoverRule rule, DateTimeOffset now)
    {
        _snoozedUntil = now + TimeSpan.FromMinutes(Math.Max(1, rule.SnoozeMinutes));
        Raised = false;
    }

    private TakeoverAction Lower()
    {
        if (!Raised) return TakeoverAction.None;
        Raised = false;
        return TakeoverAction.Clear;
    }
}

public static class Templates
{
    public static string Render(string template, IReadOnlyDictionary<string, string> tokens) =>
        tokens.Aggregate(template, (s, kv) => s.Replace("{" + kv.Key + "}", kv.Value, StringComparison.OrdinalIgnoreCase));
}
