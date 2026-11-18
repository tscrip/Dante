using Dante.Core.Config;
using Dante.Core.Widgets;
using Dante.Server.Features.DaysUntil;

namespace Dante.Tests;

// The example plugin's tests: copy this file next to a new plugin's tests.
public class DaysUntilTests
{
    // Pretend today is 16 November 2026 (ManualTime is a clock the test controls).
    private static readonly DaysUntilWidget Widget = new(new ManualTime(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero)));

    private static WidgetDefinition With(string date, string what = "Vacation")
    {
        var w = Widget.CreateDefault();
        w.Settings["event"] = what;
        w.Settings["date"] = date;
        return w;
    }

    [Theory]
    [InlineData("2026-11-28", 12, "Vacation in 12 days")]
    [InlineData("2026-11-17", 1, "Vacation is tomorrow!")]
    [InlineData("2026-11-16", 0, "Vacation is today!")]
    [InlineData("2026-11-10", 0, "Vacation was 6 days ago")]
    public async Task Counts_the_days(string date, double value, string text)
    {
        var reading = await Widget.ReadAsync(With(date), default);
        Assert.Null(reading.Error);
        Assert.Equal(value, reading.Value);
        Assert.Equal(text, reading.Text);
    }

    [Fact]
    public async Task A_bad_date_explains_the_format()
    {
        var reading = await Widget.ReadAsync(With("next friday"), default);
        Assert.Contains("year-month-day", reading.Error);
    }

    [Fact]
    public void New_widgets_are_a_counter_in_days()
    {
        var w = Widget.CreateDefault();
        Assert.Equal((VisualKind.Counter, "days", DaysUntilWidget.TypeName), (w.Visual, w.Unit, w.Type));
    }
}
