using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dante.Core.Config;

/// <summary>Loads/saves config.json and notifies listeners when it changes.</summary>
public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _gate = new();
    private DanteConfig _current;

    public ConfigStore(string path)
    {
        FilePath = path;
        _current = Load(path);
    }

    public string FilePath { get; }

    public event Action<DanteConfig>? Changed;

    /// <summary>A private deep copy — edit it freely, then pass it to <see cref="Save"/>.</summary>
    public DanteConfig Snapshot()
    {
        lock (_gate) return Clone(_current);
    }

    public void Save(DanteConfig config)
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
        var env = Environment.GetEnvironmentVariable("DANTE_CONFIG")
                  ?? Environment.GetEnvironmentVariable("HANK_CONFIG")        // earlier names of the project
                  ?? Environment.GetEnvironmentVariable("DESKBUDDY_CONFIG");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        var path = Path.Combine(root, "Dante", "config.json");
        // Newest earlier name first: someone who ran the short-lived "Hank" build has the latest settings there.
        foreach (var legacy in new[] { "Hank", "DeskBuddy" })
            if (MigrateLegacyConfig(Path.Combine(root, legacy, "config.json"), path)) break;
        return path;
    }

    /// <summary>Names the project (and the device's Bluetooth name) had before it became DANTE.</summary>
    public static readonly string[] LegacyNames = ["DeskBuddy", "HANK"];

    /// <summary>The project used to be called DeskBuddy, then HANK: carry an old config over once.</summary>
    public static bool MigrateLegacyConfig(string legacyPath, string newPath)
    {
        if (File.Exists(newPath) || !File.Exists(legacyPath)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(newPath))!);
        File.Copy(legacyPath, newPath);
        return true;
    }

    public static DanteConfig Clone(DanteConfig c) =>
        JsonSerializer.Deserialize<DanteConfig>(JsonSerializer.Serialize(c, JsonOptions), JsonOptions)!;

    /// <summary>Normalises ids (unique slugs) and clamps obviously bad numbers.</summary>
    public static void Validate(DanteConfig config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in config.Widgets)
        {
            if (string.IsNullOrWhiteSpace(app.Name)) app.Name = "Widget";
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
        MigrateLegacy(config);
        // Firmware advertised "DeskBuddy", then "HANK", before the project became DANTE.
        if (LegacyNames.Contains(config.Device.BluetoothName?.Trim() ?? "", StringComparer.OrdinalIgnoreCase))
            config.Device.BluetoothName = "DANTE";
    }

    /// <summary>Carries settings from older config layouts over to the current one (idempotent).</summary>
    public static void MigrateLegacy(DanteConfig config)
    {
        // The MAGIC button became a feature slice: its settings moved from "magic" to "features.magic".
        if (config.LegacyMagic is { } magic)
        {
            config.Features.TryAdd("magic", magic);
            config.LegacyMagic = null;
        }
        // DANTE dropped two-screen mode: keep the first screen's "upside down" setting.
        if (config.Device.LegacyFlipScreen1 is { } flip)
        {
            config.Device.Flip |= flip;
            config.Device.LegacyFlipScreen1 = null;
        }
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

    private static DanteConfig Load(string path)
    {
        if (File.Exists(path))
        {
            var json = JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (json is JsonObject root) MigrateJson(root);
            var cfg = json.Deserialize<DanteConfig>(JsonOptions) ?? new();
            Validate(cfg);
            return cfg;
        }
        return DefaultConfig.Create();
    }

    /// <summary>
    /// Changes from older versions, done on the raw JSON before it is loaded. Idempotent.
    /// <list type="bullet">
    /// <item>Plugins and widgets: "apps" became "widgets", and in each widget "enabled" became "active",
    /// "sourceType" "type" and "sourceSettings" "settings".</item>
    /// <item>The character LCDs (1602 / 2004) were removed: a config set to one goes back to automatic.</item>
    /// </list>
    /// </summary>
    public static void MigrateJson(JsonObject root)
    {
        Rename(root, "apps", "widgets");
        if (root["widgets"] is JsonArray widgets)
        {
            foreach (var w in widgets.OfType<JsonObject>())
            {
                Rename(w, "enabled", "active");
                Rename(w, "sourceType", "type");
                Rename(w, "sourceSettings", "settings");
            }
        }
        if (root["device"] is JsonObject device)
        {
            if (device["display"] is JsonValue d && d.TryGetValue<string>(out var display)
                && display.StartsWith("Lcd", StringComparison.OrdinalIgnoreCase))
                device["display"] = "Auto";
            device.Remove("lcdAddress");
            device.Remove("lcdSize");
        }
    }

    private static void Rename(JsonObject o, string from, string to)
    {
        if (!o.TryGetPropertyValue(from, out var value) || o.ContainsKey(to)) return;
        o.Remove(from);
        o[to] = value;
    }
}
