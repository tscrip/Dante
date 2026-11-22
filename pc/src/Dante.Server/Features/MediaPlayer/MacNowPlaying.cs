using System.Globalization;
using System.Text.RegularExpressions;
using Dante.Core.Media;
using Dante.Core.Os;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>
/// macOS has no public "now playing" for all apps, so this asks Spotify and Apple Music directly with
/// AppleScript (osascript). The first time, macOS asks whether DANTE's app may control them: say OK.
/// </summary>
public sealed partial class MacNowPlaying(ICommandRunner runner, IHttpClientFactory http) : INowPlayingSource
{
    private static readonly string[] Apps = ["Spotify", "Music"];

    public async Task<NowPlaying?> GetAsync(string player, CancellationToken ct)
    {
        // "is running" compiles even when an app isn't installed; talking to an app that isn't there doesn't.
        var wanted = Apps.Where(a => string.IsNullOrWhiteSpace(player) || a.Equals(player.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (wanted.Count == 0) throw new ActionFailedException("On macOS the player can be Spotify or Music");
        var running = await Run(ct, $"return {string.Join(" & \",\" & ", wanted.Select(a => $"((application \"{a}\" is running) as text)"))}");
        if (running is null) return null;
        var flags = running.Split(',');

        var tracks = new List<Track>();
        for (var i = 0; i < wanted.Count && i < flags.Length; i++)
            if (flags[i].Trim() == "true" && ParseTrack(wanted[i], await Run(ct, TrackScript(wanted[i])) ?? "") is { } t)
                tracks.Add(t);
        if ((tracks.FirstOrDefault(t => t.Playing) ?? tracks.FirstOrDefault()) is not { } track) return null;

        return track.App == "Spotify"
            ? new NowPlaying(track.Title, track.Artist, track.Playing, track.App, ArtKey: track.ArtUrl,
                LoadArt: string.IsNullOrEmpty(track.ArtUrl) ? null : c => DownloadAsync(track.ArtUrl!, c))
            : new NowPlaying(track.Title, track.Artist, track.Playing, track.App, ArtKey: $"music:{track.Title}|{track.Artist}",
                LoadArt: MusicArtworkAsync);
    }

    public async Task<bool> ControlAsync(string? app, MediaCommand command, CancellationToken ct)
    {
        if (app is not ("Spotify" or "Music")) return false;
        var verb = command switch { MediaCommand.PlayPause => "playpause", MediaCommand.Next => "next track", _ => "previous track" };
        var result = await runner.RunAsync("osascript", ["-e", $"tell application \"{app}\" to {verb}"], ct);
        return result.Ok;
    }

    internal sealed record Track(string App, string Title, string? Artist, bool Playing, string? ArtUrl);

    // One line, tab separated: state, title, artist, artwork link (Spotify only). Nothing when stopped.
    private static string TrackScript(string app) => string.Join('\n',
        $"tell application \"{app}\"",
        "  if player state is stopped then return \"\"",
        "  set t to current track",
        $"  return (player state as text) & tab & (name of t) & tab & (artist of t) & tab & {(app == "Spotify" ? "(artwork url of t)" : "\"\"")}",
        "end tell");

    /// <summary>"playing\tTitle\tArtist\thttps://..." → a track; empty or broken output → null.</summary>
    internal static Track? ParseTrack(string app, string output)
    {
        var f = output.TrimEnd('\r', '\n').Split('\t');
        if (f.Length < 3 || f[1].Length == 0) return null;
        return new Track(app, f[1], f[2].Length > 0 ? f[2] : null, f[0] == "playing",
            f.Length > 3 && f[3].StartsWith("http", StringComparison.Ordinal) ? f[3] : null);
    }

    /// <summary>Apple Music has no artwork link, only the picture itself, which AppleScript prints as «data JPEG ff d8...».</summary>
    private async Task<byte[]?> MusicArtworkAsync(CancellationToken ct) =>
        ParseAppleScriptData(await Run(ct, "tell application \"Music\" to return raw data of artwork 1 of current track") ?? "");

    internal static byte[]? ParseAppleScriptData(string output)
    {
        if (AppleScriptData().Match(output) is not { Success: true } m) return null;
        var hex = m.Groups[1].Value;
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        using var response = await http.CreateClient(nameof(MacNowPlaying)).GetAsync(url, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    private async Task<string?> Run(CancellationToken ct, string script)
    {
        var result = await runner.RunAsync("osascript", ["-e", script], ct);
        if (result.NotFound) throw new ActionFailedException("osascript is missing (it comes with macOS)");
        return result.Ok ? result.Output : null;   // e.g. not allowed to control the app yet
    }

    [GeneratedRegex(@"«data \w{4}([0-9A-Fa-f]+)»")]
    private static partial Regex AppleScriptData();
}
