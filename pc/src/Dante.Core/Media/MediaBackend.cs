using Dante.Core.Os;

namespace Dante.Core.Media;

/// <summary>
/// Media and audio controls on this PC (play/pause, next, volume, mute...), with one implementation per
/// operating system. Shared by every plugin that controls media: the Magic button and the media player.
/// </summary>
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
    /// <summary>The speakers' volume and mute state right now, or null if this PC can't tell.</summary>
    Task<VolumeState?> GetVolumeAsync(CancellationToken ct);
}

/// <summary>The speakers' volume (0-100 %) and whether they are muted.</summary>
public sealed record VolumeState(int Percent, bool Muted);

/// <summary>Raised when an action can't be done here; the message is shown on DANTE.</summary>
public sealed class ActionFailedException(string message) : Exception(message);

public static class MediaBackends
{
    /// <summary>The media controls for the operating system DANTE's PC app is running on.</summary>
    public static IMediaBackend ForThisOs(ICommandRunner runner) =>
        OperatingSystem.IsWindows() ? new WindowsMediaBackend()
        : OperatingSystem.IsMacOS() ? new MacMediaBackend(runner)
        : new LinuxMediaBackend(runner);
}
