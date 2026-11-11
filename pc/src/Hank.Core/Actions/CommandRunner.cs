using System.ComponentModel;
using System.Diagnostics;

namespace Hank.Core.Actions;

public sealed record CommandResult(int ExitCode, string Output, string Error, bool NotFound = false)
{
    public bool Ok => ExitCode == 0 && !NotFound;
}

/// <summary>Runs external programs (osascript, wpctl, ...). Swappable for tests.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Win32Exception)
        {
            return new CommandResult(-1, "", $"{file} not found", NotFound: true);
        }
        if (p is null) return new CommandResult(-1, "", $"could not start {file}", NotFound: true);

        using (p)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
                await p.WaitForExitAsync(timeout.Token);
                return new CommandResult(p.ExitCode, (await stdout).Trim(), (await stderr).Trim());
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                p.Kill(entireProcessTree: true);
                return new CommandResult(-1, "", $"{file} timed out");
            }
        }
    }
}
