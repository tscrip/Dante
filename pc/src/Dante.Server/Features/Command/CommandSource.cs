using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.Command;

/// <summary>Runs a command; stdout is either a number, "number text", or JSON {value,text}.</summary>
public sealed class CommandSource : IWidgetType
{
    public const string TypeName = "command";
    public string Type => TypeName;
    public string PluginId => CommandPlugin.PluginId;
    public string DisplayName => "Command output";
    public string Description => "Runs a program each poll. Output: a number, \"<number> <text>\", or JSON {\"value\":1,\"text\":\"...\"}.";

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("fileName", "Program", Help: "e.g. dotnet, pwsh, /usr/local/bin/mytool, or sh -c"),
        new("arguments", "Arguments", SettingKind.Multiline, Help: "One argument per line (a line can contain spaces). E.g. Program sh -c, Arguments: ls ~/inbox | wc -l"),
        new("timeoutSeconds", "Timeout (s)", SettingKind.Number, "20"),
    ];

    public async Task<WidgetReading> ReadAsync(WidgetDefinition app, CancellationToken ct)
    {
        var file = app.Get(this, "fileName");
        if (string.IsNullOrWhiteSpace(file)) return WidgetReading.Fail("Set a program.");
        var (program, args) = CommandLine.Resolve(file, app.Get(this, "arguments"));
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(int.TryParse(app.Get(this, "timeoutSeconds"), out var s) ? s : 20));
        Process? started;
        try
        {
            started = Process.Start(psi);
        }
        catch (Win32Exception)
        {
            return WidgetReading.Fail($"Can't find the program '{program}'. Is it installed and on the PATH?");
        }
        using var p = started ?? throw new InvalidOperationException("Could not start process.");
        try
        {
            var stdout = await p.StandardOutput.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            return p.ExitCode == 0 ? ParseOutput(stdout) : WidgetReading.Fail($"Exit code {p.ExitCode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            p.Kill(entireProcessTree: true);
            return WidgetReading.Fail("Timed out.");
        }
    }

    public static WidgetReading ParseOutput(string stdout)
    {
        var trimmed = stdout.Trim();
        if (trimmed.StartsWith('{'))
        {
            var json = JsonNode.Parse(trimmed);
            return new WidgetReading { Value = JsonPath.Number(json, "value"), Text = JsonPath.Text(json, "text") };
        }
        var m = Regex.Match(trimmed, @"^(-?\d+(?:\.\d+)?)\s*(.*)$", RegexOptions.Singleline);
        return m.Success
            ? new WidgetReading { Value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Text = m.Groups[2].Value.Trim() }
            : new WidgetReading { Text = trimmed };
    }
}

