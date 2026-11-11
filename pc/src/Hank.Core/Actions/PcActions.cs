using Hank.Core.Config;
using Hank.Core.Engine;

namespace Hank.Core.Actions;

/// <summary>Things the PC can do when the MAGIC button is pressed. Returns a short line for HANK.</summary>
public interface IPcActions
{
    Task<string> RunAsync(MagicAction action, CancellationToken ct);
}

/// <summary>Media and audio controls, implemented per operating system.</summary>
public interface IMediaBackend
{
    Task PlayPauseAsync(CancellationToken ct);
    Task NextAsync(CancellationToken ct);
    Task PreviousAsync(CancellationToken ct);
    /// <returns>True if the speakers are muted afterwards.</returns>
    Task<bool> ToggleSpeakerMuteAsync(CancellationToken ct);
    /// <returns>True if the microphone is muted afterwards.</returns>
    Task<bool> ToggleMicMuteAsync(CancellationToken ct);
    /// <returns>The volume in percent afterwards, if known.</returns>
    Task<int?> ChangeVolumeAsync(bool up, CancellationToken ct);
}

/// <summary>Raised when an action can't be done here; the message is shown on HANK.</summary>
public sealed class ActionFailedException(string message) : Exception(message);

public sealed class PcActions(IMediaBackend media, ICommandRunner runner, IUrlOpener urls) : IPcActions
{
    public static IMediaBackend BackendForThisOs(ICommandRunner runner) =>
        OperatingSystem.IsWindows() ? new WindowsMediaBackend()
        : OperatingSystem.IsMacOS() ? new MacMediaBackend(runner)
        : new LinuxMediaBackend(runner);

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
                var args = (action.Arguments ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var result = await runner.RunAsync(action.Target.Trim(), args, ct);
                if (result.NotFound) throw new ActionFailedException($"Can't find {Path.GetFileName(action.Target.Trim())}");
                if (result.ExitCode != 0) throw new ActionFailedException($"{Path.GetFileName(action.Target.Trim())} failed ({result.ExitCode})");
                return string.IsNullOrWhiteSpace(result.Output) ? $"Ran {Path.GetFileName(action.Target.Trim())}" : FirstLine(result.Output);
            default:
                return "";
        }
    }

    private static string Describe(string target) =>
        Uri.TryCreate(target.Trim(), UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host) ? u.Host : target.Trim();

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();
}
