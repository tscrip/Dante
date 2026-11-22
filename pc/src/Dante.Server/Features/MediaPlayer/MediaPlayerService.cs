using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;
using Dante.Core.Media;
using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>
/// The media player's brain: reads what's playing (and the speakers' volume) for the widget, keeps the
/// shrunken album art so it is only made once per song, and turns presses on DANTE's media screen into
/// commands for the PC.
/// </summary>
public sealed class MediaPlayerService(
    INowPlayingSource source,
    IMediaBackend media,
    MediaPlayerPlugin plugin,
    PluginSettings settings,
    DeviceAlerts alerts,
    ILogger<MediaPlayerService> log) : IDeviceEventHandler
{
    private readonly Lock _gate = new();
    private DanteEngine? _engine;
    private string? _app;          // the player DANTE shows, so play/pause goes to the same one
    private string? _artKey;
    private byte[]? _art;

    /// <summary>Wires the button handler up. The engine is handed over here because it needs this service's widget type.</summary>
    public void Initialize(DanteEngine engine)
    {
        _engine = engine;
        engine.AddHandler(this, 100);
    }

    /// <summary>The widget's reading: title, artist, play state, volume, mute and album art.</summary>
    public async Task<WidgetReading> ReadAsync(WidgetDefinition widget, CancellationToken ct)
    {
        NowPlaying? now;
        try { now = await source.GetAsync(settings.Get(plugin, "player", widget), ct); }
        catch (ActionFailedException ex) { return WidgetReading.Fail(ex.Message); }

        var volume = await VolumeAsync(ct);
        lock (_gate) _app = now?.App;
        var art = now is null ? null : await ArtAsync(now, ct);
        return new WidgetReading
        {
            Value = volume?.Percent,   // so a gauge or bar of this widget shows the volume
            Text = now is null ? "Nothing playing" : now.Title,
            Media = new MediaReading(now?.Artist, now?.Playing ?? false, volume?.Muted ?? false, volume?.Percent, art),
        };
    }

    private async Task<VolumeState?> VolumeAsync(CancellationToken ct)
    {
        try { return await media.GetVolumeAsync(ct); }
        catch (Exception ex) when (ex is ActionFailedException or PlatformNotSupportedException) { return null; }
    }

    /// <summary>The device-ready album art, made once per picture.</summary>
    private async Task<byte[]?> ArtAsync(NowPlaying now, CancellationToken ct)
    {
        if (now.ArtKey is null || now.LoadArt is null) return null;
        lock (_gate) if (now.ArtKey == _artKey) return _art;
        byte[]? art = null;
        try { art = AlbumArt.ToDeviceJpeg(await now.LoadArt(ct)); }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
        {
            log.LogDebug(ex, "Couldn't load album art");
        }
        // Remembered even when it failed: no point trying the same broken picture every few seconds.
        lock (_gate) { _artKey = now.ArtKey; _art = art; }
        return art;
    }

    /// <summary>
    /// Presses on the media screen. On a touch screen, taps on the on-screen controls say which one (<c>ctl</c>);
    /// with buttons, SELECT = play/pause and a long SELECT = next. MAGIC is never ours.
    /// </summary>
    public bool Handle(DeviceEvent evt, WidgetDefinition? screen)
    {
        if (evt.Type != "btn" || evt.Button != "sel" || screen?.Type != MediaPlayerWidget.TypeName) return false;
        var command = evt.Control ?? (evt.Long ? "next" : "play");
        if (command is not ("play" or "next" or "prev" or "volu" or "vold" or "mute")) return false;
        LastCommand = RunAsync(command, CancellationToken.None);
        return true;
    }

    /// <summary>The command started by the last press (for tests).</summary>
    internal Task? LastCommand { get; private set; }

    internal async Task RunAsync(string command, CancellationToken ct)
    {
        try
        {
            string? app;
            lock (_gate) app = _app;
            switch (command)
            {
                case "play": await PlayerAsync(app, MediaCommand.PlayPause, media.PlayPauseAsync, ct); break;
                case "next": await PlayerAsync(app, MediaCommand.Next, media.NextAsync, ct); break;
                case "prev": await PlayerAsync(app, MediaCommand.Previous, media.PreviousAsync, ct); break;
                case "volu": await media.ChangeVolumeAsync(up: true, ct); break;
                case "vold": await media.ChangeVolumeAsync(up: false, ct); break;
                case "mute": await media.ToggleSpeakerMuteAsync(ct); break;
            }
        }
        catch (Exception ex) when (ex is ActionFailedException or PlatformNotSupportedException)
        {
            alerts.Say(ex.Message);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Media command {Command} failed", command);
            alerts.Say("Couldn't do that :(");
        }
        // Give the player a moment to react, then show the new state straight away.
        try { await Task.Delay(TimeSpan.FromMilliseconds(350), ct); } catch (TaskCanceledException) { return; }
        _engine?.PollWidgetType(MediaPlayerWidget.TypeName);
    }

    // Straight to the player on screen if the source can; otherwise the PC's media keys.
    private async Task PlayerAsync(string? app, MediaCommand command, Func<CancellationToken, Task> mediaKey, CancellationToken ct)
    {
        if (!await source.ControlAsync(app, command, ct)) await mediaKey(ct);
    }
}
