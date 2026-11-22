using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>
/// The "Now playing" screen: title, artist, album art, volume, with play/pause, next, previous, volume and
/// mute buttons on the colour screen (tap them on a touch screen; with buttons, SELECT = play/pause).
/// </summary>
public sealed class MediaPlayerWidget(MediaPlayerService service) : IWidgetType
{
    public const string TypeName = "media-now-playing";

    public string Type => TypeName;
    public string PluginId => MediaPlayerPlugin.PluginId;
    public string DisplayName => "Now playing";
    public string Description =>
        "What's playing on your PC, with album art and on-screen controls on the 3.5\" colour screen. " +
        "Touch screen: tap the buttons. With buttons: SELECT plays / pauses, hold SELECT for the next song.";
    public IReadOnlyList<SettingField> Fields { get; } = [];
    public bool AllowMultiple => false;   // there's one PC, one set of speakers

    public WidgetDefinition CreateDefault() => new()
    {
        Name = "Now playing",
        Type = TypeName,
        Visual = VisualKind.Media,
        Unit = "%",
        Min = 0,
        Max = 100,
        PollSeconds = 3,          // songs change often and checking is cheap (no internet involved)
        QuipsEnabled = false,
    };

    public Task<WidgetReading> ReadAsync(WidgetDefinition widget, CancellationToken ct) => service.ReadAsync(widget, ct);
}
