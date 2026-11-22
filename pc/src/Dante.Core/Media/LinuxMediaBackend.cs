using System.Globalization;
using System.Text.RegularExpressions;

using Dante.Core.Os;

namespace Dante.Core.Media;

/// <summary>
/// Linux: audio via PipeWire's <c>wpctl</c>, falling back to PulseAudio's <c>pactl</c> (pipewire-pulse
/// provides it too); media via <c>playerctl</c> (MPRIS: Spotify, browsers, VLC, ...).
/// </summary>
public sealed partial class LinuxMediaBackend(ICommandRunner runner) : IMediaBackend
{
    private const string Sink = "@DEFAULT_AUDIO_SINK@", Source = "@DEFAULT_AUDIO_SOURCE@";

    public Task PlayPauseAsync(CancellationToken ct) => Player("play-pause", ct);
    public Task NextAsync(CancellationToken ct) => Player("next", ct);
    public Task PreviousAsync(CancellationToken ct) => Player("previous", ct);

    public Task<bool> ToggleSpeakerMuteAsync(CancellationToken ct) => ToggleMute(Sink, "sink", ct);
    public Task<bool> ToggleMicMuteAsync(CancellationToken ct) => ToggleMute(Source, "source", ct);

    public async Task<int?> ChangeVolumeAsync(bool up, CancellationToken ct)
    {
        var step = up ? "5%+" : "5%-";
        var wp = await runner.RunAsync("wpctl", ["set-volume", "-l", "1.0", Sink, step], ct);
        if (wp.Ok)
        {
            var get = await runner.RunAsync("wpctl", ["get-volume", Sink], ct);
            return WpctlVolume().Match(get.Output) is { Success: true } m
                ? (int)Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 100)
                : null;
        }
        if (!wp.NotFound) throw new ActionFailedException("wpctl couldn't change the volume");

        var pa = await Pactl(ct, "set-sink-volume", "@DEFAULT_SINK@", up ? "+5%" : "-5%");
        var vol = await Pactl(ct, "get-sink-volume", "@DEFAULT_SINK@");
        return PactlPercent().Match(vol) is { Success: true } p ? int.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    public async Task<VolumeState?> GetVolumeAsync(CancellationToken ct)
    {
        var wp = await runner.RunAsync("wpctl", ["get-volume", Sink], ct);
        if (wp.Ok) return ParseWpctl(wp.Output);
        var vol = await runner.RunAsync("pactl", ["get-sink-volume", "@DEFAULT_SINK@"], ct);
        var mute = await runner.RunAsync("pactl", ["get-sink-mute", "@DEFAULT_SINK@"], ct);
        return vol.Ok && PactlPercent().Match(vol.Output) is { Success: true } p
            ? new VolumeState(int.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture),
                              mute.Ok && mute.Output.Contains("yes", StringComparison.OrdinalIgnoreCase))
            : null;   // neither tool: no volume to show (not an error)
    }

    /// <summary>"Volume: 0.40 [MUTED]" → 40 %, muted.</summary>
    internal static VolumeState? ParseWpctl(string output) =>
        WpctlVolume().Match(output) is { Success: true } m
            ? new VolumeState((int)Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 100),
                              output.Contains("[MUTED]", StringComparison.Ordinal))
            : null;

    private async Task<bool> ToggleMute(string wpTarget, string paKind, CancellationToken ct)
    {
        var wp = await runner.RunAsync("wpctl", ["set-mute", wpTarget, "toggle"], ct);
        if (wp.Ok)
        {
            var get = await runner.RunAsync("wpctl", ["get-volume", wpTarget], ct);
            return get.Output.Contains("[MUTED]", StringComparison.Ordinal);
        }
        if (!wp.NotFound) throw new ActionFailedException($"wpctl couldn't toggle the {(paKind == "sink" ? "speakers" : "mic")}");

        var paTarget = paKind == "sink" ? "@DEFAULT_SINK@" : "@DEFAULT_SOURCE@";
        await Pactl(ct, $"set-{paKind}-mute", paTarget, "toggle");
        var state = await Pactl(ct, $"get-{paKind}-mute", paTarget);
        return state.Contains("yes", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> Pactl(CancellationToken ct, params string[] args)
    {
        var r = await runner.RunAsync("pactl", args, ct);
        if (r.NotFound) throw new ActionFailedException("Install wpctl (PipeWire) or pactl for audio control");
        if (!r.Ok) throw new ActionFailedException($"pactl {args[0]} failed");
        return r.Output;
    }

    private async Task Player(string command, CancellationToken ct)
    {
        var r = await runner.RunAsync("playerctl", [command], ct);
        if (r.NotFound) throw new ActionFailedException("Install playerctl for media keys");
        if (!r.Ok) throw new ActionFailedException("No media player running");
    }

    [GeneratedRegex(@"Volume:\s*([0-9.]+)")]
    private static partial Regex WpctlVolume();

    [GeneratedRegex(@"(\d+)%")]
    private static partial Regex PactlPercent();
}
