using System.Text.Json.Nodes;
using Dante.Core.Behavior;
using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Core.Device;

/// <summary>Builds the PC→device messages from docs/protocol.md, enforcing the device's string limits.</summary>
public static class DeviceProtocol
{
    public const int MaxWidgets = 12;

    public static JsonObject Hello() => new() { ["t"] = "hello" };
    public static JsonObject Ping() => new() { ["t"] = "ping" };
    /// <summary>Asks DANTE what it found at boot plus a live I2C scan of both buses.</summary>
    public static JsonObject Diag() => new() { ["t"] = "diag" };

    public static JsonObject Widgets(IEnumerable<WidgetDefinition> apps) => new()
    {
        ["t"] = "apps",
        ["apps"] = new JsonArray([.. apps.Take(MaxWidgets).Select(a => (JsonNode)new JsonObject
        {
            ["id"] = Format.ToDeviceText(a.Id, 15),
            ["name"] = Format.ToDeviceText(a.Name, 19),
            ["vis"] = VisualWireName(a.Visual),
            ["min"] = a.Min,
            ["max"] = a.Max,
            ["unit"] = Format.ToDeviceText(a.Unit, 5),
        })]),
    };

    /// <param name="timer">For countdowns: <c>v</c> becomes the seconds left, plus <c>tot</c> (whole length) and <c>run</c>.</param>
    public static JsonObject Value(string appId, double? value, string? text, int level, string? quip, TimerReading? timer = null)
    {
        var msg = new JsonObject { ["t"] = "val", ["id"] = appId, ["lvl"] = level };
        if (timer is not null)
        {
            msg["v"] = Math.Max(0, timer.RemainingSeconds);
            msg["tot"] = Math.Max(1, timer.TotalSeconds);
            msg["run"] = timer.Running;
        }
        else if (value is { } v && double.IsFinite(v))
        {
            msg["v"] = Math.Round(v, 2);
        }
        if (text is not null) msg["txt"] = Format.ToDeviceText(text, 47);
        if (!string.IsNullOrWhiteSpace(quip)) msg["quip"] = Format.ToDeviceText(quip, 63);
        return msg;
    }

    public static JsonObject Mood(MoodResult mood) => new()
    {
        ["t"] = "mood",
        ["m"] = mood.Mood.ToString().ToLowerInvariant(),
        ["n"] = mood.PendingCount,
        ["age"] = (int)mood.OldestWait.TotalMinutes,
    };

    public static JsonObject Alert(string appId, string title, string text, int level) => new()
    {
        ["t"] = "alert",
        ["id"] = appId,
        ["title"] = Format.ToDeviceText(title, 63),
        ["txt"] = Format.ToDeviceText(text, 63),
        ["lvl"] = Math.Clamp(level, 1, 2),
    };

    public static JsonObject Clear(string appId) => new() { ["t"] = "clear", ["id"] = appId };
    public static JsonObject Say(string text) => new() { ["t"] = "say", ["txt"] = Format.ToDeviceText(text, 63) };
    /// <summary>Display settings. DANTE stores them, so it boots in the right screen mode.</summary>
    public static JsonObject Cfg(DeviceSettings d) => new()
    {
        ["t"] = "cfg",
        ["bright"] = Math.Clamp(d.Brightness, 0, 255),
        ["display"] = DisplayWireName(d.Display),
        ["lcdSize"] = d.EffectiveLcdSize == LcdSize.Size20x4 ? "2004" : "1602",
        ["lcdAddr"] = Math.Clamp(d.LcdAddress, 0x08, 0x77),
        ["flip"] = d.Flip,
        ["accent"] = d.AccentRgb,   // 0xRRGGBB, used by the TFT
        ["look"] = Enum.IsDefined(d.Look) ? (int)d.Look : 0,
    };

    public static string DisplayWireName(DisplayType d) => d switch
    {
        DisplayType.Lcd1602 => "lcd1602",
        DisplayType.Lcd2004 => "lcd2004",
        DisplayType.Oled128x32 => "oled",
        DisplayType.Tft35 => "tft",
        _ => "auto",
    };

    public static string VisualWireName(VisualKind v) => v switch
    {
        VisualKind.Sparkline => "spark",
        _ => v.ToString().ToLowerInvariant(),
    };
}

/// <summary>A message from the device, parsed from one JSON line.</summary>
public sealed record DeviceEvent(string Type, JsonObject Raw)
{
    public string? Id => Raw["id"]?.GetValue<string>();
    public string? Button => Raw["b"]?.GetValue<string>();
    public bool Long => Raw["long"]?.GetValue<bool>() ?? false;
    public string? Screen => Raw["scr"]?.GetValue<string>();

    public static DeviceEvent? TryParse(string line)
    {
        try
        {
            return JsonNode.Parse(line) is JsonObject o && o["t"]?.GetValue<string>() is { } t ? new DeviceEvent(t, o) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // boot noise from the ESP32 ROM etc.
        }
    }
}
