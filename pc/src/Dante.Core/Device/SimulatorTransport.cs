using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Dante.Core.Config;

namespace Dante.Core.Device;

/// <summary>
/// Runs the DANTE simulator (the real firmware built for this PC with <c>pio run -e simulator</c>) and
/// talks to it over stdin/stdout, exactly like a USB serial link. Starting the transport = powering
/// DANTE on; disposing it = unplugging it.
/// </summary>
public sealed class SimulatorTransport(string program, SimulatedHardware hardware) : IDeviceTransport
{
    private readonly BlockingCollection<string> _lines = new();
    private readonly ConcurrentQueue<string> _stderr = new();
    private Process? _process;
    private volatile bool _exited;

    public bool IsOpen => _process is not null && !_exited;

    public void Open()
    {
        var psi = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(program) ?? "",
        };
        psi.ArgumentList.Add("--hw");
        psi.ArgumentList.Add(hardware.Argument());

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) _lines.Add(e.Data.TrimEnd('\r')); };
        p.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data is null) return;
            _stderr.Enqueue(e.Data);
            while (_stderr.Count > 20) _stderr.TryDequeue(out _);
        };
        p.Exited += (_, _) => _exited = true;
        try
        {
            p.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new IOException($"Can't start the simulator ({program}): {ex.Message}", ex);
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _process = p;
    }

    public string? ReadLine()
    {
        if (_lines.TryTake(out var line, 500)) return line;
        if (_exited) throw new IOException("The simulator stopped." + (_stderr.IsEmpty ? "" : " " + string.Join(" ", _stderr)));
        return null;
    }

    public void WriteLine(string line)
    {
        if (_process is null || _exited) throw new IOException("The simulator isn't running.");
        _process.StandardInput.Write(line + "\n");
        _process.StandardInput.Flush();
    }

    public void Dispose()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        _process.Dispose();
        _lines.Dispose();
    }
}

/// <summary>Finds the built simulator program.</summary>
public static class SimulatorLocator
{
    public const string BuildHint = "Build it once with: cd firmware && pio run -e simulator";

    private static string ProgramName => OperatingSystem.IsWindows() ? "program.exe" : "program";

    /// <summary>The configured path, or firmware/.pio/build/simulator/program found by walking up from here.</summary>
    public static string Find(string? configured, IEnumerable<string>? startDirs = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Path.GetFullPath(configured.Trim());
            return File.Exists(path) ? path : throw new FileNotFoundException($"No simulator at {path}. {BuildHint}");
        }

        var starts = startDirs ?? [Directory.GetCurrentDirectory(), AppContext.BaseDirectory];
        foreach (var start in starts)
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "firmware", ".pio", "build", "simulator", ProgramName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException($"The DANTE simulator isn't built yet. {BuildHint}");
    }
}
