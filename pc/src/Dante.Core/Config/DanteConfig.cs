using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dante.Core.Config;

/// <summary>Root of the persisted configuration (config.json).</summary>
public sealed class DanteConfig
{
    public DeviceSettings Device { get; set; } = new();
    public MoodSettings Mood { get; set; } = new();
    public Personality Personality { get; set; } = Personality.Cheerful;
    /// <summary>The widgets: screens on DANTE, each one fed by a widget type from a plugin. List order = screen order.</summary>
    public List<WidgetDefinition> Widgets { get; set; } = [];

    /// <summary>
    /// Settings owned by feature slices (Features/&lt;Name&gt; in the server), one JSON object per feature,
    /// keyed by the feature's settings key. The kernel never looks inside: a new feature adds its own
    /// settings class and reads it with <see cref="Feature{T}"/>, without touching this file.
    /// </summary>
    public Dictionary<string, JsonObject> Features { get; set; } = [];

    /// <summary>Before feature slices, the MAGIC button's settings lived at the top level ("magic").</summary>
    [JsonPropertyName("magic")]
    public JsonObject? LegacyMagic { get; set; }

    /// <summary>A copy of a feature's settings (defaults when it has none yet).</summary>
    public T Feature<T>(string key) where T : new() =>
        Features.TryGetValue(key, out var node) && node.Deserialize<T>(ConfigStore.JsonOptions) is { } value ? value : new T();

    /// <summary>Stores a feature's settings; save the config with <see cref="ConfigStore.Save"/> afterwards.</summary>
    public void SetFeature<T>(string key, T value) =>
        Features[key] = JsonSerializer.SerializeToNode(value, ConfigStore.JsonOptions)!.AsObject();
}

public sealed class DeviceSettings
{
    /// <summary>How the PC talks to DANTE.</summary>
    public ConnectionKind Connection { get; set; } = ConnectionKind.Bluetooth;
    /// <summary>Bluetooth LE: the name DANTE advertises.</summary>
    public string BluetoothName { get; set; } = "DANTE";
    /// <summary>USB: COM5, /dev/ttyACM0, /dev/cu.usbmodem1101 ...</summary>
    public string? PortName { get; set; }
    /// <summary>Simulator: which DANTE screen to pretend to have.</summary>
    public SimulatedHardware SimulatedHardware { get; set; } = SimulatedHardware.Tft;
    /// <summary>Simulator: the built program (blank = find firmware/.pio/build/simulator/program automatically).</summary>
    public string? SimulatorPath { get; set; }
    public int BaudRate { get; set; } = 115200;
    public bool AutoConnect { get; set; } = true;
    /// <summary>OLED contrast / TFT backlight, 0-255.</summary>
    public int Brightness { get; set; } = 200;
    /// <summary>
    /// The 3.5" TFT's accent colour as #RRGGBB: the DANTE title, the happy eyes, the current page dot and
    /// highlights. (The OLED can't show colour.)
    /// </summary>
    public string AccentColor { get; set; } = DefaultAccent;
    public const string DefaultAccent = "#5EEAD4";

    /// <summary>The accent as 0xRRGGBB; anything that isn't a #RRGGBB colour falls back to the default teal.</summary>
    [JsonIgnore]
    public int AccentRgb => ParseColor(AccentColor) ?? ParseColor(DefaultAccent)!.Value;

    /// <summary>"#5eead4", "5EEAD4" or "#5EA" (short form) → 0x5EEAD4; null if it isn't a colour.</summary>
    public static int? ParseColor(string? s)
    {
        var hex = (s ?? "").Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        return hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }

    /// <summary>What kind of screen DANTE has. DANTE always drives exactly one screen.</summary>
    public DisplayType Display { get; set; } = DisplayType.Auto;
    /// <summary>The screen is mounted upside down: rotate it 180°.</summary>
    public bool Flip { get; set; }

    /// <summary>DANTE's look: eye style, little habits and accessories (OLED and TFT).</summary>
    public Look Look { get; set; } = Look.Classic;

    /// <summary>How much DANTE's face moves around (hops, shakes, gliding glances). See <see cref="Motion"/>.</summary>
    public Motion Motion { get; set; } = Motion.Auto;

    /// <summary>When the notification light blinks, on DANTEs that have one (yellow board: wired to P3, IO22).</summary>
    public NotificationLight Light { get; set; } = NotificationLight.Waiting;

    /// <summary>
    /// An extra MAGIC button is wired (yellow board: P3, IO35 to GND, with a 10 kΩ resistor from IO35 to 3.3 V).
    /// Off by default: without its resistor the pin floats and would make up presses.
    /// </summary>
    public bool ExtraMagicButton { get; set; }

    /// <summary>Older configs had one flag per screen (two-screen mode); the first screen's flag carries over.</summary>
    [JsonPropertyName("flipScreen1")]
    public bool? LegacyFlipScreen1 { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionKind>))]
public enum ConnectionKind
{
    /// <summary>Bluetooth LE, Nordic UART Service (the ESP32-S3 has no Bluetooth Classic).</summary>
    Bluetooth,
    /// <summary>USB cable (serial port).</summary>
    Usb,
    /// <summary>The DANTE simulator: the real firmware running on this PC, no hardware needed.</summary>
    Simulator,
}

/// <summary>What the simulator pretends is wired up.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SimulatedHardware>))]
public enum SimulatedHardware
{
    /// <summary>An ESP32-S3 with the 3.5" TFT and four buttons.</summary>
    Tft,
    /// <summary>An ESP32-S3 with the 0.91" OLED and four buttons.</summary>
    Oled,
    /// <summary>The ESP32-3248S035C "yellow board": the 3.5" TFT with a touch panel instead of buttons.</summary>
    TouchBoard,
}

public static class SimulatedHardwareExtensions
{
    /// <summary>The simulator's --hw argument.</summary>
    public static string Argument(this SimulatedHardware hw) => hw switch
    {
        SimulatedHardware.Oled => "oled",
        SimulatedHardware.TouchBoard => "cyd",
        _ => "tft",
    };

    public static string Describe(this SimulatedHardware hw) => hw switch
    {
        SimulatedHardware.Oled => "0.91\" OLED",
        SimulatedHardware.TouchBoard => "3.5\" touch screen (yellow board)",
        _ => "3.5\" TFT",
    };

    /// <summary>The simulated board has a touch screen instead of buttons.</summary>
    public static bool HasTouch(this SimulatedHardware hw) => hw == SimulatedHardware.TouchBoard;

    /// <summary>
    /// Points the screen settings at the simulated hardware: automatic screen type, so DANTE's own
    /// detection picks it, exactly as on the board.
    /// </summary>
    public static void ApplyTo(this SimulatedHardware hw, DeviceSettings d)
    {
        d.SimulatedHardware = hw;
        d.Display = DisplayType.Auto;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<DisplayType>))]
public enum DisplayType
{
    /// <summary>
    /// DANTE uses the OLED if it answers on I2C, otherwise the SPI colour TFT (which it can't detect).
    /// </summary>
    Auto,
    /// <summary>0.91" SSD1306 128x32 OLED.</summary>
    Oled128x32,
    /// <summary>3.5" 480x320 colour TFT, ST7796 driver, SPI.</summary>
    Tft35,
}

/// <summary>
/// DANTE's look. The numeric value goes over the wire, so only append new looks at the end.
/// </summary>
/// <summary>
/// How lively the face is. Moving the whole face means redrawing (and resending) most of the screen, which
/// boards without PSRAM (the yellow ESP32-3248S035C) can't do smoothly; calm keeps the expressions, blinks
/// and glances but drops the hops, shakes and screen knocks.
/// </summary>
/// <summary>When DANTE's notification light blinks (on DANTEs that have one wired).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationLight>))]
public enum NotificationLight
{
    /// <summary>A short blink every 2 s while something is waiting, blinking during a nudge, fast when urgent.</summary>
    Waiting = 0,
    /// <summary>Only during nudges (blinking; fast when urgent).</summary>
    Nudges = 1,
    /// <summary>Never.</summary>
    Off = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<Motion>))]
public enum Motion
{
    /// <summary>DANTE decides: calm on boards that draw the colour screen in strips (no PSRAM), lively otherwise.</summary>
    Auto = 0,
    /// <summary>All the movement: hops, foot tapping, shaking, gliding glances.</summary>
    Lively = 1,
    /// <summary>Expressions, blinks and quick glances, but the face stays put: smoother on slower boards.</summary>
    Calm = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<Look>))]
public enum Look
{
    /// <summary>The original DANTE.</summary>
    Classic = 0,
    /// <summary>Curly mustache, calmly half-lidded eyes, slow blinks and unhurried glances.</summary>
    Gentleman = 1,
    /// <summary>Round eyes behind glasses; often looks up while "thinking".</summary>
    Nerd = 2,
    /// <summary>Heavy eyebrows (a built-in frown) and a bristly mustache.</summary>
    GrumpyUncle = 3,
    /// <summary>Big eyes, a party hat, happy hops and confetti.</summary>
    Party = 4,
}

public static class LookInfo
{
    public static string Title(this Look l) => l switch
    {
        Look.GrumpyUncle => "Grumpy Uncle",
        _ => l.ToString(),
    };

    public static string Describe(this Look l) => l switch
    {
        Look.Gentleman => "A curly mustache, calm half-lidded eyes and slow, deliberate blinks.",
        Look.Nerd => "Round eyes behind glasses. Often glances up while \"thinking\".",
        Look.GrumpyUncle => "Heavy eyebrows that make every mood a bit sterner, plus a bristly mustache.",
        Look.Party => "Big sparkly eyes, a party hat, and happy hops with confetti.",
        _ => "The original DANTE, as nature intended.",
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<Personality>))]
public enum Personality { Cheerful, Sarcastic, Dramatic }

/// <summary>Ordered by severity; the wire name is the lower-case enum name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Mood>))]
public enum Mood { Happy = 0, Curious = 1, Antsy = 2, Grumpy = 3, Meltdown = 4 }

public sealed class MoodSettings
{
    /// <summary>
    /// A rule fires when the pending count reaches <see cref="MoodRule.MinItems"/> OR the oldest
    /// pending item has waited <see cref="MoodRule.MinOldestMinutes"/>. The most severe firing rule wins.
    /// </summary>
    public List<MoodRule> Rules { get; set; } =
    [
        new() { Mood = Mood.Curious,  MinItems = 1 },
        new() { Mood = Mood.Antsy,    MinItems = 3, MinOldestMinutes = 30 },
        new() { Mood = Mood.Grumpy,   MinItems = 5, MinOldestMinutes = 120 },
        new() { Mood = Mood.Meltdown, MinItems = 8, MinOldestMinutes = 480 },
    ];
}

public sealed class MoodRule
{
    public Mood Mood { get; set; }
    /// <summary>0 disables the count condition.</summary>
    public int MinItems { get; set; }
    /// <summary>0 disables the age condition.</summary>
    public int MinOldestMinutes { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<VisualKind>))]
public enum VisualKind
{
    Gauge, Bar, Counter, Text, Sparkline, Status,
    /// <summary>A countdown (mm:ss and a progress bar). The value is the seconds left; DANTE counts down by itself while it runs.</summary>
    Timer,
    /// <summary>
    /// Two mini history graphs, one above the other, each with its label and current value (e.g. CPU and
    /// memory). The reading's <c>Value</c> is the top line and <c>Value2</c> the bottom; both use Min..Max.
    /// </summary>
    DualSparkline,
    /// <summary>
    /// A media player: what's playing (title, artist, album art on the colour screen), play/pause and volume,
    /// with on-screen controls on touch screens. Needs a widget that gives a <c>MediaReading</c>.
    /// </summary>
    Media,
}

[JsonConverter(typeof(JsonStringEnumConverter<TakeoverMode>))]
public enum TakeoverMode
{
    Never,
    /// <summary>Whenever the source reports at least one pending item (or a non-zero value).</summary>
    WhenPending,
    WhenAbove,
    WhenBelow,
}

/// <summary>
/// A widget: one screen (and its nudges) on DANTE, made from one of the widget types a plugin offers
/// (e.g. the Claude Code plugin offers "5-hour usage" and "Sessions waiting"). Called an "app" on the wire.
/// </summary>
public sealed class WidgetDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Only active widgets run: they are polled, show on DANTE, nudge and count toward the mood.</summary>
    public bool Active { get; set; } = true;

    // ---- What it looks like
    public VisualKind Visual { get; set; } = VisualKind.Counter;
    public double Min { get; set; }
    public double Max { get; set; } = 100;
    public string Unit { get; set; } = "";
    /// <summary>Dual sparkline: the short labels of the two lines (up to 4 characters, e.g. "CPU" and "MEM").</summary>
    public string Label1 { get; set; } = "";
    public string Label2 { get; set; } = "";

    // ---- Where the data comes from (runs on the PC)
    /// <summary>The widget type (<see cref="Widgets.IWidgetType.Type"/>), e.g. "claude-code-usage".</summary>
    public string Type { get; set; } = "push";
    /// <summary>This widget's values for its type's <see cref="Widgets.IWidgetType.Fields"/>.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];
    public int PollSeconds { get; set; } = 60;

    // ---- What happens
    /// <summary>Has its own screen in the LEFT/RIGHT rotation. Off = it only nudges and affects the mood.</summary>
    public bool ShowInCarousel { get; set; } = true;
    public bool CountsTowardMood { get; set; }
    public LevelRule Levels { get; set; } = new();
    public TakeoverRule Takeover { get; set; } = new();
    /// <summary>Opened on the PC when SELECT is pressed on this app's screen. Supports {url} = first pending item's link.</summary>
    public string? OnSelectUrl { get; set; }

    // ---- Personality
    public bool QuipsEnabled { get; set; } = true;
    /// <summary>A quip is delivered when the value moves at least this much (or the level changes).</summary>
    public double QuipMinDelta { get; set; } = 1;
    /// <summary>Optional custom lines. Tokens: {name} {value} {delta} {unit} {count} {text}.</summary>
    public List<string> IncreaseQuips { get; set; } = [];
    public List<string> DecreaseQuips { get; set; } = [];
}

/// <summary>Maps a value to ok / warn / crit. Null thresholds are ignored.</summary>
public sealed class LevelRule
{
    public double? WarnAt { get; set; }
    public double? CritAt { get; set; }
    public bool HigherIsWorse { get; set; } = true;
}

public sealed class TakeoverRule
{
    public TakeoverMode Mode { get; set; } = TakeoverMode.Never;
    public double Threshold { get; set; }
    /// <summary>The condition must hold this long before DANTE nudges.</summary>
    public int DelaySeconds { get; set; }
    /// <summary>After an "I'm on it" ack, nudge again after this many minutes if still true. 0 = not until the condition resets.</summary>
    public int RenudgeMinutes { get; set; } = 10;
    public int SnoozeMinutes { get; set; } = 5;
    /// <summary>1 = nudge, 2 = urgent (screen flashes).</summary>
    public int Level { get; set; } = 1;
    /// <summary>Tokens: {name} {value} {unit} {text} {count} {item} {oldest}.</summary>
    public string? Message { get; set; }
}
