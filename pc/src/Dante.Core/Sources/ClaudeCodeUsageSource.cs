using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Dante.Core.Config;

namespace Dante.Core.Sources;

/// <summary>
/// Subscription usage (the numbers behind Claude Code's <c>/usage</c>): utilisation % of the
/// rolling 5-hour or weekly window.
/// <para>
/// NOTE: this uses the same OAuth usage endpoint Claude Code itself calls. It is not a documented
/// public API and may change; if it breaks, switch the app to the "push" or "command" source.
/// </para>
/// </summary>
public sealed class ClaudeCodeUsageSource(IHttpClientFactory http, TimeProvider time) : IDataSource
{
    public const string TypeName = "claude-code-usage";
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    public string Type => TypeName;
    public string DisplayName => "Claude Code: subscription usage";
    public string Description =>
        "Percentage of your Claude subscription window used (what /usage shows). Reads the Claude Code login " +
        "from ~/.claude/.credentials.json (or the macOS keychain). Unofficial endpoint - may change.";

    public IReadOnlyList<SettingField> Settings { get; } =
    [
        new("window", "Usage window", SettingKind.Choice, "five_hour", Choices: ["five_hour", "seven_day", "seven_day_opus"]),
        new("credentialsPath", "Credentials file", SettingKind.Text, "",
            "Blank = ~/.claude/.credentials.json (macOS: keychain item 'Claude Code-credentials')."),
        new("token", "Access token override", SettingKind.Secret, "", "Optional. Use env:VAR_NAME to read it from an environment variable."),
    ];

    public async Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct)
    {
        var token = app.Get(this, "token");
        if (string.IsNullOrWhiteSpace(token))
            token = await ReadLocalTokenAsync(app.Get(this, "credentialsPath"), ct);
        if (string.IsNullOrWhiteSpace(token))
            return SourceReading.Fail("No Claude Code login found - run `claude` and log in, or set a token.");

        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        req.Headers.UserAgent.ParseAdd("DANTE/1.0");

        using var resp = await http.CreateClient(nameof(ClaudeCodeUsageSource)).SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return SourceReading.Fail($"Usage endpoint returned {(int)resp.StatusCode}. Try `claude` to refresh your login.");

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct)) as JsonObject;
        return Parse(json, app.Get(this, "window"), time.GetUtcNow());
    }

    public static SourceReading Parse(JsonObject? json, string window, DateTimeOffset now)
    {
        if (json?[window] is not JsonObject w)
            return SourceReading.Fail($"Window '{window}' not in response (not on this plan?).");

        var utilization = w["utilization"]?.GetValue<double>() ?? 0;
        string text = "";
        if (w["resets_at"]?.GetValue<string>() is { } resets && DateTimeOffset.TryParse(resets, out var at))
            text = $"resets in {Format.Age(at - now)}";
        return new SourceReading { Value = Math.Round(utilization, 1, MidpointRounding.AwayFromZero), Text = text };
    }

    private static async Task<string?> ReadLocalTokenAsync(string configuredPath, CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json")
            : configuredPath;

        string? raw = null;
        if (File.Exists(path))
            raw = await File.ReadAllTextAsync(path, ct);
        else if (OperatingSystem.IsMacOS())
            raw = await ReadMacKeychainAsync(ct);

        if (raw is null) return null;
        return (JsonNode.Parse(raw)?["claudeAiOauth"]?["accessToken"])?.GetValue<string>();
    }

    private static async Task<string?> ReadMacKeychainAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo("security", ["find-generic-password", "-s", "Claude Code-credentials", "-w"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return p.ExitCode == 0 ? output.Trim() : null;
    }
}
