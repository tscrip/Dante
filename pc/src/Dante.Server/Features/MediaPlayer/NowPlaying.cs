using Dante.Core.Media;
using Dante.Core.Os;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>
/// What a media player is playing right now.
/// </summary>
/// <param name="App">Which player it is, as the source names it (used to send play/pause to the same player).</param>
/// <param name="ArtKey">Identifies the album art (a URL, or the track): the picture is only loaded again when it changes.</param>
/// <param name="LoadArt">Fetches the album art (any image format); null when there is none.</param>
public sealed record NowPlaying(
    string Title,
    string? Artist,
    bool Playing,
    string? App = null,
    string? ArtKey = null,
    Func<CancellationToken, Task<byte[]?>>? LoadArt = null);

/// <summary>Play/pause, next or previous: the commands a player itself understands.</summary>
public enum MediaCommand { PlayPause, Next, Previous }

/// <summary>
/// Where "now playing" comes from: one implementation per operating system (and, one day, a phone or a
/// Spotify account). Volume and mute are the PC's speakers, handled by <see cref="IMediaBackend"/>.
/// </summary>
public interface INowPlayingSource
{
    /// <summary>What's playing, or null if nothing is.</summary>
    /// <param name="player">The player to follow (the plugin's setting); empty = whatever is playing.</param>
    /// <exception cref="ActionFailedException">Something to fix on this PC, in plain words (a missing tool...).</exception>
    Task<NowPlaying?> GetAsync(string player, CancellationToken ct);

    /// <summary>Sends a command straight to <paramref name="app"/> (the player DANTE shows).</summary>
    /// <returns>False if this source can't: the PC's media keys are used instead.</returns>
    Task<bool> ControlAsync(string? app, MediaCommand command, CancellationToken ct);
}

public static class NowPlayingSources
{
    /// <summary>The "now playing" source for the operating system DANTE's PC app runs on.</summary>
    public static INowPlayingSource ForThisOs(ICommandRunner runner, IHttpClientFactory http, ILoggerFactory logs) =>
        OperatingSystem.IsWindows() ? new WindowsNowPlaying(logs.CreateLogger<WindowsNowPlaying>())
        : OperatingSystem.IsMacOS() ? new MacNowPlaying(runner, http)
        : new LinuxNowPlaying(runner, http);
}
