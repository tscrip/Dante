using Dante.Core.Config;

namespace Dante.Core.Widgets;

/// <summary>
/// A kind of widget a plugin offers, e.g. the Claude Code plugin's "5-hour usage". It does all the work on
/// the PC and boils it down to one reading (a number, a short text, optionally the things waiting on the
/// human, or a countdown). The human adds widgets of this type and sets each one's <see cref="Fields"/>.
/// </summary>
public interface IWidgetType
{
    /// <summary>Stable id stored in config (e.g. "azure-devops-prs").</summary>
    string Type { get; }
    /// <summary>The <see cref="Plugins.IPlugin.Id"/> this widget type belongs to.</summary>
    string PluginId { get; }
    string DisplayName { get; }
    string Description { get; }
    /// <summary>Settings each widget of this type has (shared settings belong on the plugin).</summary>
    IReadOnlyList<SettingField> Fields { get; }
    /// <summary>Push-style types update instantly; the poll interval is just a safety net.</summary>
    bool IsPushBased => false;
    /// <summary>False for things there is only one of (the Pomodoro timer, the next meeting).</summary>
    bool AllowMultiple => true;

    /// <summary>A new widget of this type, with sensible looks and behaviour.</summary>
    WidgetDefinition CreateDefault() => new() { Name = DisplayName, Type = Type };

    /// <summary>An optional sample shown under the description in the widget editor (e.g. how to push a value).</summary>
    string? Example(WidgetDefinition widget) => null;

    Task<WidgetReading> ReadAsync(WidgetDefinition widget, CancellationToken ct);
}

public enum SettingKind { Text, Secret, Number, Bool, Choice, Multiline }

public sealed record SettingField(
    string Key,
    string Label,
    SettingKind Kind = SettingKind.Text,
    string? Default = null,
    string? Help = null,
    IReadOnlyList<string>? Choices = null);

/// <summary>Something that needs the human, used for the mood and for takeovers.</summary>
public sealed record PendingItem(string Key, string Title, DateTimeOffset Since, string? Url = null);

public sealed record WidgetReading
{
    public double? Value { get; init; }
    /// <summary>A second value, for <see cref="VisualKind.DualSparkline"/> (the bottom line).</summary>
    public double? Value2 { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<PendingItem>? Pending { get; init; }
    /// <summary>For countdowns (<see cref="VisualKind.Timer"/>): DANTE ticks the clock down by itself between readings.</summary>
    public TimerReading? Timer { get; init; }
    public string? Error { get; init; }

    public static WidgetReading Fail(string error) => new() { Error = error };

    /// <summary>What the warn / critical levels look at: with two values, whichever is higher.</summary>
    public double? LevelValue => Value is { } a && Value2 is { } b ? Math.Max(a, b) : Value ?? Value2;
}

/// <summary>A countdown: seconds left, the length of the whole countdown (for the progress bar), and whether it is ticking.</summary>
public sealed record TimerReading(int RemainingSeconds, int TotalSeconds, bool Running);

public static class WidgetSettingsExtensions
{
    /// <summary>Reads a setting, falling back to the field default. "env:NAME" resolves an environment variable.</summary>
    public static string Get(this WidgetDefinition app, IWidgetType source, string key)
    {
        app.Settings.TryGetValue(key, out var value);
        if (string.IsNullOrEmpty(value))
            value = source.Fields.FirstOrDefault(f => f.Key == key)?.Default ?? "";
        return ResolveSecret(value);
    }

    public static string ResolveSecret(string value)
    {
        if (!value.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) return value;
        var name = value[4..].Trim();
        return Environment.GetEnvironmentVariable(name)
               ?? RenamedVariables(name).Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => v is not null)
               ?? "";
    }

    /// <summary>
    /// The project was DeskBuddy, then HANK, now DANTE: DANTE_X, HANK_X and DESKBUDDY_X stand in for each
    /// other (the exact name always wins), so old configs and old environment variables keep working.
    /// </summary>
    private static readonly string[] Prefixes = ["DANTE_", "HANK_", "DESKBUDDY_"];

    private static IEnumerable<string> RenamedVariables(string name)
    {
        var prefix = Prefixes.FirstOrDefault(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (prefix is null) yield break;
        foreach (var other in Prefixes.Where(p => p != prefix)) yield return other + name[prefix.Length..];
    }
}

/// <summary>All registered widget types, looked up by type.</summary>
public sealed class WidgetTypeRegistry(IEnumerable<IWidgetType> types)
{
    private readonly Dictionary<string, IWidgetType> _byType =
        types.ToDictionary(s => s.Type, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IWidgetType> All => _byType.Values;

    public IReadOnlyList<IWidgetType> ForPlugin(string pluginId) =>
        [.. _byType.Values.Where(t => t.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.DisplayName)];

    public IWidgetType? Find(string type) => _byType.GetValueOrDefault(type);
}
