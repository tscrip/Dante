using Hank.Core.Config;

namespace Hank.Core.Sources;

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
               ?? Environment.GetEnvironmentVariable(RenamedVariable(name))
               ?? "";
    }

    /// <summary>The project used to be DeskBuddy: HANK_X and DESKBUDDY_X stand in for each other.</summary>
    private static string RenamedVariable(string name) =>
        name.StartsWith("HANK_", StringComparison.OrdinalIgnoreCase) ? "DESKBUDDY_" + name[5..]
        : name.StartsWith("DESKBUDDY_", StringComparison.OrdinalIgnoreCase) ? "HANK_" + name[10..]
        : name;
}

/// <summary>All registered sources, looked up by type.</summary>
public sealed class SourceRegistry(IEnumerable<IDataSource> sources)
{
    private readonly Dictionary<string, IDataSource> _byType =
        sources.ToDictionary(s => s.Type, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IDataSource> All => _byType.Values;

    public IDataSource? Find(string type) => _byType.GetValueOrDefault(type);
}
