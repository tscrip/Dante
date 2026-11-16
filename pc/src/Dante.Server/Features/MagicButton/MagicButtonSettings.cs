using System.Text.Json.Serialization;

namespace Dante.Server.Features.MagicButton;

/// <summary>What the MAGIC button on top of DANTE does on the PC. Stored in config.json under features.magic.</summary>
public sealed class MagicButtonSettings
{
    public const string Key = "magic";

    public MagicAction Press { get; set; } = new() { Kind = MagicActionKind.PlayPause };
    public MagicAction LongPress { get; set; } = new() { Kind = MagicActionKind.ToggleMicMute };
}

[JsonConverter(typeof(JsonStringEnumConverter<MagicActionKind>))]
public enum MagicActionKind
{
    Nothing,
    PlayPause,
    NextTrack,
    PreviousTrack,
    ToggleSpeakerMute,
    ToggleMicMute,
    VolumeUp,
    VolumeDown,
    /// <summary>Opens <see cref="MagicAction.Target"/>: a web page, a file, or an app link like spotify: or ms-teams:.</summary>
    OpenUrl,
    /// <summary>Runs <see cref="MagicAction.Target"/> with <see cref="MagicAction.Arguments"/> (one per line).</summary>
    RunCommand,
    /// <summary>Re-polls every app on DANTE right now.</summary>
    RefreshApps,
}

public sealed class MagicAction
{
    public MagicActionKind Kind { get; set; } = MagicActionKind.Nothing;
    /// <summary>OpenUrl: the URL. RunCommand: the program.</summary>
    public string? Target { get; set; }
    /// <summary>RunCommand: one argument per line.</summary>
    public string? Arguments { get; set; }
    /// <summary>Show the result ("Mic muted") in a speech bubble on DANTE.</summary>
    public bool ConfirmOnBuddy { get; set; } = true;
}

