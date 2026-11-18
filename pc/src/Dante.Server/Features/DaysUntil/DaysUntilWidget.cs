using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.DaysUntil;

/// <summary>
/// A widget type: one kind of screen the plugin can put on DANTE. People add as many widgets of this type
/// as they like, each with its own <see cref="Fields"/> (here: what the event is and when).
/// </summary>
/// <param name="time">The clock. Asked for here (instead of using DateTime.Now) so tests can set the date.</param>
public sealed class DaysUntilWidget(TimeProvider time) : IWidgetType
{
    /// <summary>Stable id stored with each widget in config.json. Never change it once people use it.</summary>
    public const string TypeName = "days-until";

    public string Type => TypeName;
    public string PluginId => DaysUntilPlugin.PluginId;
    public string DisplayName => "Countdown to a date";
    public string Description => "The number of days until a date, e.g. \"Vacation in 12 days\".";

    /// <summary>The settings each widget has. The widget editor shows them as a form automatically.</summary>
    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("event", "What", SettingKind.Text, "Vacation", "Shown on DANTE, e.g. Vacation, Launch, Mom's birthday."),
        new("date", "Date", SettingKind.Text, "", "Year-month-day, e.g. 2026-12-24."),
    ];

    /// <summary>What a new widget of this type looks like before the person changes anything.</summary>
    public WidgetDefinition CreateDefault() => new()
    {
        Name = "Countdown",
        Type = TypeName,
        Visual = VisualKind.Counter,   // a big number that bounces when it changes
        Unit = "days",
        Min = 0,
        Max = 365,
        PollSeconds = 3600,            // the answer changes once a day; checking hourly is plenty
        QuipsEnabled = false,
    };

    /// <summary>
    /// Works out what DANTE shows right now. Runs on the PC every <see cref="WidgetDefinition.PollSeconds"/>.
    /// Return a value (the number), a short text (max 47 characters on DANTE), or Fail("message") on a problem:
    /// the message shows next to the widget in the PC app.
    /// </summary>
    public Task<WidgetReading> ReadAsync(WidgetDefinition widget, CancellationToken ct)
    {
        var what = widget.Get(this, "event");
        var dateText = widget.Get(this, "date");
        if (!DateOnly.TryParseExact(dateText.Trim(), "yyyy-MM-dd", out var date))
            return Task.FromResult(WidgetReading.Fail("Set the date as year-month-day, e.g. 2026-12-24."));

        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var days = date.DayNumber - today.DayNumber;
        var text = days switch
        {
            > 1 => $"{what} in {days} days",
            1 => $"{what} is tomorrow!",
            0 => $"{what} is today!",
            -1 => $"{what} was yesterday",
            _ => $"{what} was {-days} days ago",
        };
        return Task.FromResult(new WidgetReading { Value = Math.Max(days, 0), Text = text });
    }
}
