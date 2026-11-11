using Dante.Core.Config;

namespace Dante.Core.Sources;

/// <summary>
/// A data source does all the processing on the PC and boils it down to one reading
/// (a number, a short text and optionally the list of things waiting on the human).
/// </summary>
public interface IDataSource
{
    /// <summary>Stable id stored in config (e.g. "azure-devops-prs").</summary>
    string Type { get; }
    string DisplayName { get; }
    string Description { get; }
    IReadOnlyList<SettingField> Settings { get; }
    /// <summary>Push-style sources update instantly; the poll interval is just a safety net.</summary>
    bool IsPushBased => false;

    Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct);
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

public sealed record SourceReading
{
    public double? Value { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<PendingItem>? Pending { get; init; }
    public string? Error { get; init; }

    public static SourceReading Fail(string error) => new() { Error = error };
}

public static class SourceSettingsExtensions
{
    /// <summary>Reads a setting, falling back to the field default. "env:NAME" resolves an environment variable.</summary>
    public static string Get(this AppDefinition app, IDataSource source, string key)
    {
        app.SourceSettings.TryGetValue(key, out var value);
        if (string.IsNullOrEmpty(value))
            value = source.Settings.FirstOrDefault(f => f.Key == key)?.Default ?? "";
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

/// <summary>All registered sources, looked up by type.</summary>
public sealed class SourceRegistry(IEnumerable<IDataSource> sources)
{
    private readonly Dictionary<string, IDataSource> _byType =
        sources.ToDictionary(s => s.Type, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IDataSource> All => _byType.Values;

    public IDataSource? Find(string type) => _byType.GetValueOrDefault(type);
}
