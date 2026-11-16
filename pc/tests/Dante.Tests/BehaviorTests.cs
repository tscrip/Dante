using Dante.Core.Behavior;
using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Tests;

public class MoodCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly MoodSettings Defaults = new();

    private static List<PendingItem> Items(int count, int oldestMinutes) =>
        [.. Enumerable.Range(0, count).Select(i => new PendingItem($"k{i}", "t", Now.AddMinutes(i == 0 ? -oldestMinutes : 0)))];

    [Theory]
    [InlineData(0, 0, Mood.Happy)]
    [InlineData(1, 0, Mood.Curious)]
    [InlineData(2, 10, Mood.Curious)]
    [InlineData(3, 0, Mood.Antsy)]      // by count
    [InlineData(1, 30, Mood.Antsy)]     // by age
    [InlineData(5, 0, Mood.Grumpy)]
    [InlineData(1, 121, Mood.Grumpy)]
    [InlineData(2, 600, Mood.Meltdown)]
    [InlineData(9, 0, Mood.Meltdown)]
    public void Default_thresholds(int count, int oldestMinutes, Mood expected) =>
        Assert.Equal(expected, MoodCalculator.Evaluate(Defaults, Items(count, oldestMinutes), Now).Mood);

    [Fact]
    public void Zero_disables_a_condition()
    {
        var settings = new MoodSettings { Rules = [new MoodRule { Mood = Mood.Grumpy, MinItems = 0, MinOldestMinutes = 60 }] };
        Assert.Equal(Mood.Happy, MoodCalculator.Evaluate(settings, Items(50, 5), Now).Mood);
        Assert.Equal(Mood.Grumpy, MoodCalculator.Evaluate(settings, Items(1, 61), Now).Mood);
    }

    [Fact]
    public void Reports_count_and_oldest()
    {
        var result = MoodCalculator.Evaluate(Defaults, Items(4, 42), Now);
        Assert.Equal(4, result.PendingCount);
        Assert.Equal(42, (int)result.OldestWait.TotalMinutes);
    }
}

public class LevelEvaluatorTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData(10.0, 0)]
    [InlineData(70.0, 1)]
    [InlineData(95.0, 2)]
    public void Higher_is_worse(double? value, int expected) =>
        Assert.Equal(expected, LevelEvaluator.Evaluate(new LevelRule { WarnAt = 70, CritAt = 90 }, value));

    [Theory]
    [InlineData(50.0, 0)]
    [InlineData(20.0, 1)]
    [InlineData(5.0, 2)]
    public void Lower_is_worse(double value, int expected) =>
        Assert.Equal(expected, LevelEvaluator.Evaluate(new LevelRule { WarnAt = 20, CritAt = 10, HigherIsWorse = false }, value));
}

public class TakeoverTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly TakeoverRule Rule = new()
    {
        Mode = TakeoverMode.WhenPending, DelaySeconds = 20, RenudgeMinutes = 5, SnoozeMinutes = 3,
    };

    [Fact]
    public void Waits_for_delay_then_raises_once()
    {
        var t = new TakeoverTracker();
        Assert.Equal(TakeoverAction.None, t.Evaluate(Rule, true, T0));
        Assert.Equal(TakeoverAction.None, t.Evaluate(Rule, true, T0.AddSeconds(19)));
        Assert.Equal(TakeoverAction.Raise, t.Evaluate(Rule, true, T0.AddSeconds(20)));
        Assert.Equal(TakeoverAction.None, t.Evaluate(Rule, true, T0.AddSeconds(25)));
        Assert.True(t.Raised);
    }

    [Fact]
    public void Clears_when_condition_goes_away()
    {
        var t = new TakeoverTracker();
        t.Evaluate(Rule, true, T0);
        t.Evaluate(Rule, true, T0.AddSeconds(30));
        Assert.Equal(TakeoverAction.Clear, t.Evaluate(Rule, false, T0.AddSeconds(31)));
        Assert.Equal(TakeoverAction.None, t.Evaluate(Rule, false, T0.AddSeconds(32)));
    }

    [Fact]
    public void Ack_silences_until_renudge()
    {
        var t = new TakeoverTracker();
        t.Evaluate(Rule, true, T0);
        t.Evaluate(Rule, true, T0.AddSeconds(30));
        t.Ack(T0.AddSeconds(31));
        Assert.Equal(TakeoverAction.None, t.Evaluate(Rule, true, T0.AddMinutes(4)));
        Assert.Equal(TakeoverAction.Raise, t.Evaluate(Rule, true, T0.AddSeconds(31).AddMinutes(5)));
    }

    [Fact]
    public void Ack_with_zero_renudge_waits_for_condition_to_reset()
    {
        var rule = new TakeoverRule { Mode = TakeoverMode.WhenPending, RenudgeMinutes = 0 };
        var t = new TakeoverTracker();
        Assert.Equal(TakeoverAction.Raise, t.Evaluate(rule, true, T0));
        t.Ack(T0);
        Assert.Equal(TakeoverAction.None, t.Evaluate(rule, true, T0.AddHours(5)));
        t.Evaluate(rule, false, T0.AddHours(6));
        Assert.Equal(TakeoverAction.Raise, t.Evaluate(rule, true, T0.AddHours(7)));
    }

    [Fact]
    public void Snooze_silences_for_snooze_minutes()
    {
        var t = new TakeoverTracker();
        t.Evaluate(Rule, true, T0);
        t.Evaluate(Rule, true, T0.AddSeconds(30));
        t.Snooze(Rule, T0.AddMinutes(1));
        Assert.Equal(TakeoverAction.None, t.Evaluate(Rule, true, T0.AddMinutes(3)));
        Assert.Equal(TakeoverAction.Raise, t.Evaluate(Rule, true, T0.AddMinutes(4)));
    }

    [Theory]
    [InlineData(TakeoverMode.WhenAbove, 80, 85.0, true)]
    [InlineData(TakeoverMode.WhenAbove, 80, 50.0, false)]
    [InlineData(TakeoverMode.WhenBelow, 10, 5.0, true)]
    [InlineData(TakeoverMode.WhenPending, 0, 2.0, true)]
    [InlineData(TakeoverMode.WhenPending, 0, 0.0, false)]
    [InlineData(TakeoverMode.Never, 0, 99.0, false)]
    public void Conditions(TakeoverMode mode, double threshold, double value, bool expected) =>
        Assert.Equal(expected, TakeoverTracker.ConditionHolds(new TakeoverRule { Mode = mode, Threshold = threshold }, new WidgetReading { Value = value }));

    [Fact]
    public void Pending_list_wins_over_value()
    {
        var rule = new TakeoverRule { Mode = TakeoverMode.WhenPending };
        Assert.False(TakeoverTracker.ConditionHolds(rule, new WidgetReading { Value = 5, Pending = [] }));
    }
}

public class QuipBankTests
{
    private static readonly WidgetDefinition App = new() { Name = "PRs", QuipMinDelta = 1 };

    [Fact]
    public void No_quip_on_first_reading_or_small_change()
    {
        Assert.Null(QuipBank.Pick(Personality.Cheerful, new QuipContext(App, null, 3, 0, 0, null), new Random(1)));
        var gauge = new WidgetDefinition { Name = "Usage", QuipMinDelta = 10 };
        Assert.Null(QuipBank.Pick(Personality.Cheerful, new QuipContext(gauge, 40, 45, 0, 0, null), new Random(1)));
    }

    [Fact]
    public void Level_change_always_quips()
    {
        var gauge = new WidgetDefinition { Name = "Usage", QuipMinDelta = 50 };
        Assert.NotNull(QuipBank.Pick(Personality.Dramatic, new QuipContext(gauge, 68, 71, 0, 1, null), new Random(1)));
    }

    [Fact]
    public void Custom_lines_and_tokens()
    {
        var app = new WidgetDefinition { Name = "PRs", IncreaseQuips = ["{delta} more {name}, now {value}!"] };
        Assert.Equal("2 more PRs, now 5!", QuipBank.Pick(Personality.Sarcastic, new QuipContext(app, 3, 5, 0, 0, null), new Random(1)));
    }

    [Fact]
    public void Disabled_means_silent() =>
        Assert.Null(QuipBank.Pick(Personality.Cheerful, new QuipContext(new WidgetDefinition { QuipsEnabled = false }, 1, 9, 0, 2, null), new Random(1)));
}
