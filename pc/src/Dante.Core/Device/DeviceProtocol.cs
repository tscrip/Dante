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
        ["apps"] = new JsonArray([.. apps.Take(MaxWidgets).Select(a => (JsonNode)WidgetJson(a))]),
    };

    private static JsonObject WidgetJson(WidgetDefinition a)
    {
        var o = new JsonObject
        {
            ["id"] = Format.ToDeviceText(a.Id, 15),
            ["name"] = Format.ToDeviceText(a.Name, 19),
            ["vis"] = VisualWireName(a.Visual),
            ["min"] = a.Min,
            ["max"] = a.Max,
            ["unit"] = Format.ToDeviceText(a.Unit, 5),
        };
        if (a.Visual == VisualKind.DualSparkline)
        {
            o["l1"] = Format.ToDeviceText(string.IsNullOrWhiteSpace(a.Label1) ? "A" : a.Label1.Trim(), 4);
            o["l2"] = Format.ToDeviceText(string.IsNullOrWhiteSpace(a.Label2) ? "B" : a.Label2.Trim(), 4);
        }
        return o;
    }

    /// <param name="timer">For countdowns: <c>v</c> becomes the seconds left, plus <c>tot</c> (whole length) and <c>run</c>.</param>
    /// <param name="value2">The bottom line of a dual sparkline.</param>
    public static JsonObject Value(string appId, double? value, string? text, int level, string? quip, TimerReading? timer = null, double? value2 = null)
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
        if (value2 is { } v2 && double.IsFinite(v2)) msg["v2"] = Math.Round(v2, 2);
        if (text is not null) msg["txt"] = Format.ToDeviceText(text, 47);
        if (!string.IsNullOrWhiteSpace(quip)) msg["quip"] = Format.ToDeviceText(quip, 63);
        return msg;
    }

    /// <summary>A <see cref="Value"/> message plus what a media player is doing (<c>sub</c>, <c>play</c>, <c>mute</c>, <c>vol</c>, <c>art</c>).</summary>
    public static JsonObject Value(string appId, WidgetReading? reading, int level, string? quip)
    {
        var msg = Value(appId, reading?.Value, reading?.Text, level, quip, reading?.Timer, reading?.Value2);
        if (reading?.Media is { } m)
        {
            msg["sub"] = Format.ToDeviceText(m.Artist ?? "", 47);
            msg["play"] = m.Playing;
            msg["mute"] = m.Muted;
            if (m.Volume is { } vol) msg["vol"] = Math.Clamp(vol, 0, 100);
            if (ArtFits(m.Art)) msg["art"] = ArtHash(m.Art!);   // which picture to show: sent with Art() when it changes
        }
        return msg;
    }

    /// <summary>The largest album art DANTE accepts (it has little memory): a ~120 px JPEG is 4-8 KB.</summary>
    public const int MaxArtBytes = 12 * 1024;
    private const int ArtChunkBytes = 1024;   // ~1.4 KB of base64 per message, well under DANTE's 2 KB line limit

    public static bool ArtFits(byte[]? art) => art is { Length: > 0 and <= MaxArtBytes };

    /// <summary>A short fingerprint of a picture, so DANTE can tell whether it already has it.</summary>
    public static string ArtHash(byte[] art) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(art))[..8].ToLowerInvariant();

    /// <summary>
    /// A widget's picture (a JPEG) as <c>art</c> messages: <c>i</c> of <c>n</c> chunks of base64 in <c>d</c>,
    /// all tagged with the picture's hash <c>h</c>. DANTE puts it together and shows it once all chunks arrived.
    /// </summary>
    public static IEnumerable<JsonObject> Art(string appId, byte[] jpeg)
    {
        if (!ArtFits(jpeg)) yield break;
        var hash = ArtHash(jpeg);
        var count = (jpeg.Length + ArtChunkBytes - 1) / ArtChunkBytes;
        for (var i = 0; i < count; i++)
        {
            var part = jpeg.AsSpan(i * ArtChunkBytes, Math.Min(ArtChunkBytes, jpeg.Length - i * ArtChunkBytes));
            yield return new JsonObject
            {
                ["t"] = "art", ["id"] = appId, ["h"] = hash, ["i"] = i, ["n"] = count, ["len"] = jpeg.Length,
                ["d"] = Convert.ToBase64String(part),
            };
        }
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
        ["flip"] = d.Flip,
        ["accent"] = d.AccentRgb,   // 0xRRGGBB, used by the TFT
        ["look"] = Enum.IsDefined(d.Look) ? (int)d.Look : 0,
    };

    public static string DisplayWireName(DisplayType d) => d switch
    {
        DisplayType.Oled128x32 => "oled",
        DisplayType.Tft35 => "tft",
        _ => "auto",
    };

    public static string VisualWireName(VisualKind v) => v switch
    {
        VisualKind.Sparkline => "spark",
        VisualKind.DualSparkline => "spark2",
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
    /// <summary>
    /// Which on-screen control a tap hit, for screens with controls on touch screens (the media player:
    /// prev, play, next, vold, volu, mute). Null for a plain button press.
    /// </summary>
    public string? Control => Raw["ctl"]?.GetValue<string>();

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
