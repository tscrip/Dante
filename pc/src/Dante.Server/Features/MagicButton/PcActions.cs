using Dante.Core.Engine;
using Dante.Core.Media;
using Dante.Core.Os;

namespace Dante.Server.Features.MagicButton;

/// <summary>Things the PC can do when the MAGIC button is pressed. Returns a short line for DANTE.</summary>
public interface IPcActions
{
    Task<string> RunAsync(MagicAction action, CancellationToken ct);
}

public sealed class PcActions(IMediaBackend media, ICommandRunner runner, IUrlOpener urls) : IPcActions
{
    public async Task<string> RunAsync(MagicAction action, CancellationToken ct)
    {
        switch (action.Kind)
        {
            case MagicActionKind.PlayPause: await media.PlayPauseAsync(ct); return "Play / pause";
            case MagicActionKind.NextTrack: await media.NextAsync(ct); return "Next track >>";
            case MagicActionKind.PreviousTrack: await media.PreviousAsync(ct); return "<< Previous track";
            case MagicActionKind.ToggleSpeakerMute: return await media.ToggleSpeakerMuteAsync(ct) ? "Sound muted" : "Sound on";
            case MagicActionKind.ToggleMicMute: return await media.ToggleMicMuteAsync(ct) ? "Mic muted" : "Mic is live!";
            case MagicActionKind.VolumeUp:
            case MagicActionKind.VolumeDown:
                var up = action.Kind == MagicActionKind.VolumeUp;
                var level = await media.ChangeVolumeAsync(up, ct);
                return level is { } l ? $"Volume {l}%" : up ? "Volume up" : "Volume down";
            case MagicActionKind.OpenUrl:
                if (string.IsNullOrWhiteSpace(action.Target)) throw new ActionFailedException("No URL set for the magic button");
                urls.Open(action.Target.Trim());
                return $"Opening {Describe(action.Target)}";
            case MagicActionKind.RunCommand:
                if (string.IsNullOrWhiteSpace(action.Target)) throw new ActionFailedException("No program set for the magic button");
                var (file, args) = Command.CommandLine.Resolve(action.Target, action.Arguments);
                var name = Path.GetFileName(file);
                var result = await runner.RunAsync(file, args, ct);
                if (result.NotFound) throw new ActionFailedException($"Can't find {name}");
                if (result.ExitCode != 0) throw new ActionFailedException($"{name} failed ({result.ExitCode})");
                return string.IsNullOrWhiteSpace(result.Output) ? $"Ran {name}" : FirstLine(result.Output);
            default:
                return "";
        }
    }

    private static string Describe(string target) =>
        Uri.TryCreate(target.Trim(), UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host) ? u.Host : target.Trim();

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();
}
