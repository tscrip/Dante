using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dante.Core.Media;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>
/// Windows: reads the system's media sessions, the same thing the volume pop-up shows (Spotify, browsers,
/// VLC, Windows Media Player...), including album art. Those are Windows Runtime APIs, which this
/// cross-platform app can't call directly, so a small PowerShell helper (built into Windows) runs in the
/// background and prints a line whenever what's playing changes. Play/pause/next/previous use the media
/// keys (<see cref="IMediaBackend"/>), which go to the same session.
/// </summary>
public sealed class WindowsNowPlaying(ILogger<WindowsNowPlaying> log) : INowPlayingSource, IDisposable
{
    private readonly Lock _gate = new();
    private Process? _helper;
    private string? _helperPlayer;
    private DateTimeOffset _lastStart = DateTimeOffset.MinValue;
    private HelperState? _state;
    private string? _lastError;

    public Task<NowPlaying?> GetAsync(string player, CancellationToken ct)
    {
        EnsureHelper(player.Trim());
        HelperState? state;
        lock (_gate) state = _state;
        if (state is null)
            return _lastError is { } err ? throw new ActionFailedException(err) : Task.FromResult<NowPlaying?>(null);   // still starting
        if (!state.Ok) throw new ActionFailedException($"Windows media info isn't available: {state.Error}");
        if (state.None || string.IsNullOrEmpty(state.Title)) return Task.FromResult<NowPlaying?>(null);

        var art = state.Art is { Length: > 0 } b64 ? Convert.FromBase64String(b64) : null;
        // The key includes the picture's size: Windows often hands over the art a moment after the title.
        return Task.FromResult<NowPlaying?>(new NowPlaying(state.Title, string.IsNullOrEmpty(state.Artist) ? null : state.Artist,
            state.Playing, state.App, ArtKey: art is null ? null : $"{state.ArtKey}:{art.Length}",
            LoadArt: art is null ? null : _ => Task.FromResult<byte[]?>(art)));
    }

    public Task<bool> ControlAsync(string? app, MediaCommand command, CancellationToken ct) => Task.FromResult(false);

    /// <summary>One line from the helper.</summary>
    internal sealed record HelperState(bool Ok, bool None, string? App, string? Title, string? Artist, bool Playing, string? ArtKey, string? Art, string? Error);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal static HelperState? ParseLine(string line)
    {
        try { return JsonSerializer.Deserialize<HelperState>(line, Json); }
        catch (JsonException) { return null; }   // PowerShell noise (warnings), not ours
    }

    private void EnsureHelper(string player)
    {
        lock (_gate)
        {
            if (_helper is { HasExited: false } && _helperPlayer == player) return;
            if (DateTimeOffset.UtcNow - _lastStart < TimeSpan.FromSeconds(10)) return;   // don't respawn in a tight loop
            _lastStart = DateTimeOffset.UtcNow;
            StopHelper();
            _state = null;
            _helperPlayer = player;
            try
            {
                var psi = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                             Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) })
                    psi.ArgumentList.Add(a);
                psi.Environment["DANTE_PLAYER"] = player;
                var p = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell didn't start");
                p.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is { Length: > 0 } line && ParseLine(line) is { } s)
                        lock (_gate) { _state = s; _lastError = null; }
                };
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log.LogDebug("Media helper: {Line}", e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                _helper = p;
            }
            catch (Exception ex)
            {
                _lastError = "Couldn't start PowerShell to read what's playing";
                log.LogWarning(ex, "Media helper failed to start");
            }
        }
    }

    private void StopHelper()
    {
        try { if (_helper is { HasExited: false }) _helper.Kill(); }
        catch (InvalidOperationException) { }   // already gone
        _helper?.Dispose();
        _helper = null;
    }

    public void Dispose()
    {
        lock (_gate) StopHelper();
    }

    /// <summary>
    /// Windows PowerShell 5.1 (powershell.exe, part of Windows) can use Windows Runtime types. Every second it
    /// looks at the current media session (or the one whose app id contains DANTE_PLAYER) and prints one JSON
    /// line when anything changed. The album art (a small picture Windows keeps per track) goes along as base64.
    /// </summary>
    internal const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
        $null = [Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType = WindowsRuntime]
        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        } | Select-Object -First 1
        function Await($op, [Type]$type) {
            $task = $asTask.MakeGenericMethod($type).Invoke($null, @($op))
            $null = $task.Wait(5000)
            $task.Result
        }
        $player = $env:DANTE_PLAYER
        $manager = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
        $last = ''; $artFor = ''; $art = $null; $artTries = 0
        while ($true) {
            try {
                if ($player) { $session = $manager.GetSessions() | Where-Object { $_.SourceAppUserModelId -like "*$player*" } | Select-Object -First 1 }
                else { $session = $manager.GetCurrentSession() }
                if (-not $session) { $state = @{ ok = $true; none = $true } }
                else {
                    $props = Await ($session.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
                    $playing = $session.GetPlaybackInfo().PlaybackStatus -eq [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus]::Playing
                    $key = "$($session.SourceAppUserModelId)|$($props.Title)|$($props.Artist)"
                    if ($key -ne $artFor) { $artFor = $key; $art = $null; $artTries = 0 }
                    if (-not $art -and $props.Thumbnail -and $artTries -lt 5) {
                        $artTries++
                        try {
                            $stream = Await ($props.Thumbnail.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType])
                            $net = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
                            $ms = New-Object System.IO.MemoryStream
                            $net.CopyTo($ms); $net.Dispose()
                            if ($ms.Length -gt 0) { $art = [Convert]::ToBase64String($ms.ToArray()) }
                        } catch { }
                    }
                    $state = @{ ok = $true; none = $false; app = $session.SourceAppUserModelId; title = $props.Title; artist = $props.Artist;
                                playing = $playing; artKey = $artFor; art = $art }
                }
            } catch { $state = @{ ok = $false; error = $_.Exception.Message } }
            $json = $state | ConvertTo-Json -Compress
            if ($json -ne $last) { [Console]::Out.WriteLine($json); [Console]::Out.Flush(); $last = $json }
            Start-Sleep -Milliseconds 1000
        }
        """;
}
