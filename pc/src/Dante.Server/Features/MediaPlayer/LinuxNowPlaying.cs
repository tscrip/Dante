using Dante.Core.Media;
using Dante.Core.Os;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>
/// Linux: asks <c>playerctl</c>, which speaks MPRIS, the standard every Linux media player (Spotify,
/// browsers, VLC, Rhythmbox...) offers. Album art comes as a file:// or https:// link.
/// </summary>
public sealed class LinuxNowPlaying(ICommandRunner runner, IHttpClientFactory http) : INowPlayingSource
{
    // Fields are separated by the "unit separator" control character, which never appears in a song title.
    private const char Sep = '\u001f';
    private static readonly string Format = string.Join(Sep, "{{playerName}}", "{{status}}", "{{title}}", "{{artist}}", "{{mpris:artUrl}}");
    private const long MaxArtDownload = 5 * 1024 * 1024;

    public async Task<NowPlaying?> GetAsync(string player, CancellationToken ct)
    {
        // A named player, or every player (-a): then the one that is playing wins.
        List<string> args = string.IsNullOrWhiteSpace(player) ? ["-a"] : ["-p", player.Trim()];
        args.AddRange(["metadata", "--format", Format]);
        var result = await runner.RunAsync("playerctl", args, ct);
        if (result.NotFound) throw new ActionFailedException("Install playerctl to see what's playing (e.g. sudo apt install playerctl)");
        if (!result.Ok) return null;   // "No players found"
        if (Pick(result.Output) is not { } t) return null;

        var url = t.ArtUrl;
        return new NowPlaying(t.Title, NullIfEmpty(t.Artist), t.Status == "Playing", t.Player,
            ArtKey: NullIfEmpty(url), LoadArt: string.IsNullOrEmpty(url) ? null : c => LoadArtAsync(url, c));
    }

    public async Task<bool> ControlAsync(string? app, MediaCommand command, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(app)) return false;
        var verb = command switch { MediaCommand.PlayPause => "play-pause", MediaCommand.Next => "next", _ => "previous" };
        var result = await runner.RunAsync("playerctl", ["-p", app, verb], ct);
        if (result.NotFound) throw new ActionFailedException("Install playerctl for media controls");
        return result.Ok;
    }

    internal sealed record Track(string Player, string Status, string Title, string Artist, string ArtUrl);

    /// <summary>One line per player: the one that is playing, else a paused one; stopped players don't count.</summary>
    internal static Track? Pick(string output)
    {
        var tracks = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(Sep))
            .Where(f => f.Length >= 5)
            .Select(f => new Track(f[0], f[1], f[2], f[3], f[4]))
            .Where(t => t.Status != "Stopped" && t.Title.Length > 0)
            .ToList();
        return tracks.FirstOrDefault(t => t.Status == "Playing") ?? tracks.FirstOrDefault();
    }

    private async Task<byte[]?> LoadArtAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.IsFile) return File.Exists(uri.LocalPath) ? await File.ReadAllBytesAsync(uri.LocalPath, ct) : null;
        if (uri.Scheme is not ("http" or "https")) return null;
        using var response = await http.CreateClient(nameof(LinuxNowPlaying)).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxArtDownload) return null;
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
