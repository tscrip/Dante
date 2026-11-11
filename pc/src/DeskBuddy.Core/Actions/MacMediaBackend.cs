using System.Globalization;

namespace DeskBuddy.Core.Actions;

/// <summary>
/// macOS: audio via AppleScript (osascript). Media keys are posted as system-defined events through
/// JavaScript for Automation, which needs Accessibility permission for the app running DeskBuddy
/// (System Settings → Privacy &amp; Security → Accessibility).
/// </summary>
public sealed class MacMediaBackend(ICommandRunner runner) : IMediaBackend
{
    // NX_KEYTYPE_* from IOKit/hidsystem/ev_keymap.h
    private const int NX_KEYTYPE_PLAY = 16, NX_KEYTYPE_NEXT = 17, NX_KEYTYPE_PREVIOUS = 18;
    private int _micLevelBeforeMute = 75;

    public Task PlayPauseAsync(CancellationToken ct) => MediaKey(NX_KEYTYPE_PLAY, ct);
    public Task NextAsync(CancellationToken ct) => MediaKey(NX_KEYTYPE_NEXT, ct);
    public Task PreviousAsync(CancellationToken ct) => MediaKey(NX_KEYTYPE_PREVIOUS, ct);

    public async Task<bool> ToggleSpeakerMuteAsync(CancellationToken ct)
    {
        var output = await AppleScript(ct,
            "set volume output muted not (output muted of (get volume settings))",
            "return output muted of (get volume settings)");
        return output.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>macOS has no mic mute switch, so "muted" = input volume 0, restoring the old level after.</summary>
    public async Task<bool> ToggleMicMuteAsync(CancellationToken ct)
    {
        var current = int.TryParse(await AppleScript(ct, "return input volume of (get volume settings)"), out var v) ? v : 0;
        if (current > 0)
        {
            _micLevelBeforeMute = current;
            await AppleScript(ct, "set volume input volume 0");
            return true;
        }
        await AppleScript(ct, $"set volume input volume {_micLevelBeforeMute}");
        return false;
    }

    public async Task<int?> ChangeVolumeAsync(bool up, CancellationToken ct)
    {
        var output = await AppleScript(ct,
            $"set volume output volume ((output volume of (get volume settings)) {(up ? "+" : "-")} 6)",
            "return output volume of (get volume settings)");
        return int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? level : null;
    }

    private async Task MediaKey(int key, CancellationToken ct)
    {
        // Key down then key up, as the keyboard's media keys do.
        var script = string.Join('\n',
            "ObjC.import('Cocoa');",
            $"[[0xa00, 0xa], [0xb00, 0xb]].forEach(([flags, state]) => {{",
            $"  const ev = $.NSEvent.otherEventWithTypeLocationModifierFlagsTimestampWindowNumberContextSubtypeData1Data2(",
            $"    14, $.NSZeroPoint, flags, 0, 0, null, 8, ({key} << 16) | (state << 8), -1);",
            "  $.CGEventPost(0, ev.CGEvent);",
            "});");
        var result = await runner.RunAsync("osascript", ["-l", "JavaScript", "-e", script], ct);
        if (!result.Ok) throw new ActionFailedException("Media key failed - allow DeskBuddy in Accessibility settings");
    }

    private async Task<string> AppleScript(CancellationToken ct, params string[] lines)
    {
        var args = new List<string>();
        foreach (var line in lines) { args.Add("-e"); args.Add(line); }
        var result = await runner.RunAsync("osascript", args, ct);
        if (!result.Ok) throw new ActionFailedException("osascript failed: " + (result.Error.Length > 0 ? result.Error : result.ExitCode.ToString()));
        return result.Output.Trim();
    }
}
