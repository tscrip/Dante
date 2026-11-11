using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hank.Core.Actions;

/// <summary>
/// Windows: media keys via keybd_event (any player that listens to them: Spotify, browsers, ...),
/// mute/volume via the Core Audio API on the default devices, so we can report the new state.
/// </summary>
public sealed class WindowsMediaBackend : IMediaBackend
{
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0, VK_MEDIA_PREV_TRACK = 0xB1, VK_MEDIA_PLAY_PAUSE = 0xB3;

    public Task PlayPauseAsync(CancellationToken ct) => Key(VK_MEDIA_PLAY_PAUSE);
    public Task NextAsync(CancellationToken ct) => Key(VK_MEDIA_NEXT_TRACK);
    public Task PreviousAsync(CancellationToken ct) => Key(VK_MEDIA_PREV_TRACK);

    public Task<bool> ToggleSpeakerMuteAsync(CancellationToken ct) => Task.FromResult(CoreAudio.ToggleMute(capture: false));
    public Task<bool> ToggleMicMuteAsync(CancellationToken ct) => Task.FromResult(CoreAudio.ToggleMute(capture: true));
    public Task<int?> ChangeVolumeAsync(bool up, CancellationToken ct) => Task.FromResult<int?>(CoreAudio.Step(up));

    private static Task Key(byte vk)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        return Task.CompletedTask;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}

/// <summary>Minimal Core Audio (MMDevice API) interop: default endpoint mute + volume.</summary>
internal static class CoreAudio
{
    private const int CLSCTX_ALL = 0x17;

    /// <returns>Muted afterwards.</returns>
    public static bool ToggleMute(bool capture)
    {
        var volume = Endpoint(capture);
        Check(volume.GetMute(out var muted));
        var ctx = Guid.Empty;
        Check(volume.SetMute(!muted, ref ctx));
        return !muted;
    }

    /// <returns>Volume percent afterwards.</returns>
    public static int Step(bool up)
    {
        var volume = Endpoint(capture: false);
        var ctx = Guid.Empty;
        Check(up ? volume.VolumeStepUp(ref ctx) : volume.VolumeStepDown(ref ctx));
        Check(volume.GetMasterVolumeLevelScalar(out var level));
        return (int)Math.Round(level * 100);
    }

    private static IAudioEndpointVolume Endpoint(bool capture)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        // Microphone: the "communications" device (what Teams/Zoom use). Speakers: the multimedia one.
        if (enumerator.GetDefaultAudioEndpoint(capture ? EDataFlow.Capture : EDataFlow.Render,
                capture ? ERole.Communications : ERole.Multimedia, out var device) != 0 || device is null)
            throw new ActionFailedException(capture ? "No microphone found" : "No speakers found");
        var iid = typeof(IAudioEndpointVolume).GUID;
        Check(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj));
        return (IAudioEndpointVolume)obj;
    }

    private static void Check(int hr)
    {
        if (hr < 0) throw new ActionFailedException($"Windows audio error 0x{hr:X8}");
    }

    private enum EDataFlow { Render = 0, Capture = 1 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject;

    // Interfaces list methods in vtable order; only the ones up to what we call are needed.
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
    }
}
