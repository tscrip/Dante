using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeskBuddy.Core.ClaudeCode;

/// <summary>
/// Adds (or removes) the hooks that let Claude Code tell DeskBuddy when it's waiting on you.
/// Each hook pipes the event JSON (stdin) to the local DeskBuddy server with curl.
/// </summary>
public sealed class ClaudeHookInstaller(string settingsPath, string serverUrl)
{
    public const string Marker = "/api/hooks/claude-code";

    /// <summary>Hook events we listen to. PostToolUse needs a matcher; the rest don't.</summary>
    public static readonly string[] Events = ["Notification", "Stop", "UserPromptSubmit", "PostToolUse", "SessionStart", "SessionEnd"];

    public static string DefaultSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public string SettingsPath => settingsPath;

    public string Command =>
        $"curl -s -m 2 -X POST -H \"Content-Type: application/json\" --data-binary @- {serverUrl.TrimEnd('/')}{Marker} >/dev/null 2>&1 || true";

    /// <summary>The JSON block to paste manually, if preferred.</summary>
    public string Snippet()
    {
        var hooks = new JsonObject();
        foreach (var evt in Events) hooks[evt] = new JsonArray(Entry(evt));
        return new JsonObject { ["hooks"] = hooks }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public bool IsInstalled()
    {
        var root = Load();
        return Events.All(e => FindOurs(root, e).Any());
    }

    /// <returns>Path of the backup that was written, if any.</returns>
    public string? Install()
    {
        var root = Load();
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        foreach (var evt in Events)
        {
            if (FindOurs(root, evt).Any()) continue;
            var list = hooks[evt] as JsonArray ?? new JsonArray();
            hooks[evt] = list;
            list.Add(Entry(evt));
        }
        return Save(root);
    }

    public string? Uninstall()
    {
        var root = Load();
        if (root["hooks"] is not JsonObject hooks) return null;
        foreach (var evt in Events)
        {
            if (hooks[evt] is not JsonArray list) continue;
            foreach (var entry in FindOurs(root, evt).ToList()) list.Remove(entry);
            if (list.Count == 0) hooks.Remove(evt);
        }
        return Save(root);
    }

    private JsonObject Entry(string evt)
    {
        var entry = new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command, ["timeout"] = 5 }),
        };
        if (evt == "PostToolUse") entry["matcher"] = "*";
        return entry;
    }

    private static IEnumerable<JsonObject> FindOurs(JsonObject root, string evt) =>
        (root["hooks"]?[evt] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(e => (e["hooks"] as JsonArray ?? []).OfType<JsonObject>()
                .Any(h => h["command"]?.GetValue<string>().Contains(Marker) == true));

    private JsonObject Load() =>
        File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is JsonObject o
            ? o
            : new JsonObject();

    private string? Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        string? backup = null;
        if (File.Exists(settingsPath))
        {
            backup = settingsPath + ".deskbuddy.bak";
            File.Copy(settingsPath, backup, overwrite: true);
        }
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return backup;
    }
}
