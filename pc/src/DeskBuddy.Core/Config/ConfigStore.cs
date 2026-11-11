using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskBuddy.Core.Config;

/// <summary>Loads/saves config.json and notifies listeners when it changes.</summary>
public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _gate = new();
    private DeskBuddyConfig _current;

    public ConfigStore(string path)
    {
        FilePath = path;
        _current = Load(path);
    }

    public string FilePath { get; }

    public event Action<DeskBuddyConfig>? Changed;

    /// <summary>A private deep copy — edit it freely, then pass it to <see cref="Save"/>.</summary>
    public DeskBuddyConfig Snapshot()
    {
        lock (_gate) return Clone(_current);
    }

    public void Save(DeskBuddyConfig config)
    {
        Validate(config);
        var copy = Clone(config);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(copy, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
            _current = copy;
        }
        Changed?.Invoke(Clone(copy));
    }

    public static string DefaultPath()
    {
        var env = Environment.GetEnvironmentVariable("DESKBUDDY_CONFIG");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "DeskBuddy", "config.json");
    }

    public static DeskBuddyConfig Clone(DeskBuddyConfig c) =>
        JsonSerializer.Deserialize<DeskBuddyConfig>(JsonSerializer.Serialize(c, JsonOptions), JsonOptions)!;

    /// <summary>Normalises ids (unique slugs) and clamps obviously bad numbers.</summary>
    public static void Validate(DeskBuddyConfig config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in config.Apps)
        {
            if (string.IsNullOrWhiteSpace(app.Name)) app.Name = "App";
            var id = Slug(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
            var unique = id;
            for (var n = 2; !seen.Add(unique); n++)
                unique = $"{id[..Math.Min(id.Length, 12)]}_{n}";
            app.Id = unique;
            app.PollSeconds = Math.Clamp(app.PollSeconds, 2, 86_400);
            if (app.Max <= app.Min) app.Max = app.Min + 1;
            app.Takeover.Level = Math.Clamp(app.Takeover.Level, 1, 2);
        }
        config.Device.Brightness = Math.Clamp(config.Device.Brightness, 0, 255);
        config.Device.ScreenCount = config.Device.ScreenCount == 2 ? 2 : 1;
        config.Device.LcdAddress = Math.Clamp(config.Device.LcdAddress, 0x08, 0x77);
        config.Device.LcdSize = config.Device.EffectiveLcdSize;   // remember the size of an explicitly picked LCD
    }

    public static string Slug(string s)
    {
        var chars = s.Trim().ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var slug = new string(chars).Trim('_');
        while (slug.Contains("__")) slug = slug.Replace("__", "_");
        if (slug.Length == 0) slug = "app";
        return slug.Length > 15 ? slug[..15].TrimEnd('_') : slug;
    }

    private static DeskBuddyConfig Load(string path)
    {
        if (File.Exists(path))
        {
            var cfg = JsonSerializer.Deserialize<DeskBuddyConfig>(File.ReadAllText(path), JsonOptions) ?? new();
            Validate(cfg);
            return cfg;
        }
        return DefaultConfig.Create();
    }
}
