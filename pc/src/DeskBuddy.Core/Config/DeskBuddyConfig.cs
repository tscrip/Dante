using System.Text.Json.Serialization;

namespace DeskBuddy.Core.Config;

/// <summary>Root of the persisted configuration (config.json).</summary>
public sealed class DeskBuddyConfig
{
    public DeviceSettings Device { get; set; } = new();
    public MoodSettings Mood { get; set; } = new();
    public Personality Personality { get; set; } = Personality.Cheerful;
    public List<AppDefinition> Apps { get; set; } = [];
}

public sealed class DeviceSettings
{
    /// <summary>COM3, /dev/rfcomm0, /dev/cu.DeskBuddy, /dev/ttyUSB0 ...</summary>
    public string? PortName { get; set; }
    public int BaudRate { get; set; } = 115200;
    public bool AutoConnect { get; set; } = true;
    /// <summary>OLED contrast 0-255.</summary>
    public int Brightness { get; set; } = 200;

    /// <summary>What kind of screen the buddy has.</summary>
    public DisplayType Display { get; set; } = DisplayType.Oled128x32;
    /// <summary>I2C address of the LCD's PCF8574 backpack (usually 0x27, or 0x3F for the PCF8574A).</summary>
    public int LcdAddress { get; set; } = 0x27;

    /// <summary>OLED only: 1 = both eyes on one screen; 2 = one screen per eye.</summary>
    public int ScreenCount { get; set; } = 1;
    /// <summary>Two screens only: what app screens look like.</summary>
    public DualLayout DualLayout { get; set; } = DualLayout.Watch;
    /// <summary>Two screens, <see cref="DualLayout.Watch"/>: which screen shows the app (the other one watches it).</summary>
    public ScreenSide ContentScreen { get; set; } = ScreenSide.Right;
    /// <summary>The screens are wired the other way round: swap left and right.</summary>
    public bool SwapScreens { get; set; }
    /// <summary>Rotate screen 1 (the one on the first I2C bus) by 180°.</summary>
    public bool FlipScreen1 { get; set; }
    /// <summary>Rotate screen 2 by 180°.</summary>
    public bool FlipScreen2 { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<DisplayType>))]
public enum DisplayType
{
    /// <summary>0.91" SSD1306 128x32 OLED (one, or two for one-eye-per-screen).</summary>
    Oled128x32,
    /// <summary>16x2 HD44780 character LCD with a PCF8574 I2C backpack ("1602 I2C").</summary>
    Lcd1602,
}

[JsonConverter(typeof(JsonStringEnumConverter<DualLayout>))]
public enum DualLayout
{
    /// <summary>One screen shows the app, the other is an eye that keeps watching it.</summary>
    Watch,
    /// <summary>Both screens show the app.</summary>
    Mirror,
}

[JsonConverter(typeof(JsonStringEnumConverter<ScreenSide>))]
public enum ScreenSide { Left, Right }

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
public enum VisualKind { Gauge, Bar, Counter, Text, Sparkline, Status }

[JsonConverter(typeof(JsonStringEnumConverter<TakeoverMode>))]
public enum TakeoverMode
{
    Never,
    /// <summary>Whenever the source reports at least one pending item (or a non-zero value).</summary>
    WhenPending,
    WhenAbove,
    WhenBelow,
}

/// <summary>One "app" — a screen on the buddy fed by a data source running on the PC.</summary>
public sealed class AppDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // ---- What it looks like
    public VisualKind Visual { get; set; } = VisualKind.Counter;
    public double Min { get; set; }
    public double Max { get; set; } = 100;
    public string Unit { get; set; } = "";

    // ---- Where the data comes from
    public string SourceType { get; set; } = "push";
    public Dictionary<string, string> SourceSettings { get; set; } = [];
    public int PollSeconds { get; set; } = 60;

    // ---- What happens
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
    /// <summary>The condition must hold this long before the buddy nudges.</summary>
    public int DelaySeconds { get; set; }
    /// <summary>After an "I'm on it" ack, nudge again after this many minutes if still true. 0 = not until the condition resets.</summary>
    public int RenudgeMinutes { get; set; } = 10;
    public int SnoozeMinutes { get; set; } = 5;
    /// <summary>1 = nudge, 2 = urgent (screen flashes).</summary>
    public int Level { get; set; } = 1;
    /// <summary>Tokens: {name} {value} {unit} {text} {count} {item} {oldest}.</summary>
    public string? Message { get; set; }
}
