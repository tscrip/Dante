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
    public int BaudRate { get; set; } = 115200;
    public bool AutoConnect { get; set; } = true;
    /// <summary>OLED contrast / TFT backlight, 0-255.</summary>
    public int Brightness { get; set; } = 200;

    /// <summary>What kind of screen DANTE has. DANTE always drives exactly one screen.</summary>
    public DisplayType Display { get; set; } = DisplayType.Auto;
    /// <summary>The screen is mounted upside down: rotate it 180°.</summary>
    public bool Flip { get; set; }

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
