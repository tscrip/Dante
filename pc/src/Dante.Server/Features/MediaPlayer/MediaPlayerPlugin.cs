using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>
/// A remote for whatever is playing on the PC: what's on (title, artist, album art), play/pause, next,
/// previous, volume and mute. Works with the media players the operating system knows about.
/// </summary>
public sealed class MediaPlayerPlugin : IPlugin
{
    public const string PluginId = "media-player";

    public string Id => PluginId;
    public string Name => "Media player";
    public string Description =>
        "A remote for the music on your PC: DANTE shows what's playing (with album art on the colour screen) and lets you " +
        "play, pause, skip, change the volume and mute. Windows: any player in the volume pop-up (Spotify, browsers, VLC...). " +
        "Linux: any MPRIS player, through playerctl. macOS: Spotify and Apple Music.";
    public int Order => 60;

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("player", "Player to follow", SettingKind.Text, "",
            "Leave empty to follow whatever is playing. Or name one: Windows, part of the app's name (Spotify, Chrome, VLC); " +
            "Linux, its playerctl name (spotify, firefox, vlc; `playerctl -l` lists them); macOS, Spotify or Music."),
    ];
}
