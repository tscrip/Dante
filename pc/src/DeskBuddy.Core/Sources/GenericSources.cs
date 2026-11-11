using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DeskBuddy.Core.Config;

namespace DeskBuddy.Core.Sources;

/// <summary>Values pushed by anything over REST: POST /api/apps/{id}/value.</summary>
public sealed class PushStore
{
    private readonly ConcurrentDictionary<string, SourceReading> _readings = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? Pushed;

    public void Set(string appId, SourceReading reading)
    {
        _readings[appId] = reading;
        Pushed?.Invoke(appId);
    }

    public SourceReading? Get(string appId) => _readings.GetValueOrDefault(appId);
}

public sealed class PushSource(PushStore store) : IDataSource
{
    public const string TypeName = "push";
    public string Type => TypeName;
    public string DisplayName => "Pushed via REST API";
    public string Description =>
        "Any script or tool POSTs {\"value\":3,\"text\":\"...\",\"pending\":[{\"key\":\"a\",\"title\":\"...\",\"since\":\"2026-01-01T10:00:00Z\"}]} " +
        "to http://localhost:5280/api/apps/{id}/value.";
    public bool IsPushBased => true;
    public IReadOnlyList<SettingField> Settings { get; } = [];

    public Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct) =>
        Task.FromResult(store.Get(app.Id) ?? new SourceReading { Text = "waiting for data" });
}

/// <summary>Polls any JSON endpoint and picks fields with a dotted path (e.g. "data.items[0].count").</summary>
public sealed class HttpJsonSource(IHttpClientFactory http) : IDataSource
{
    public const string TypeName = "http-json";
    public string Type => TypeName;
    public string DisplayName => "HTTP JSON endpoint";
    public string Description => "GETs a URL and reads a number (and optional text) using dotted paths like data.items[0].count. Use \"$count:path\" to count an array.";

    public IReadOnlyList<SettingField> Settings { get; } =
    [
        new("url", "URL"),
        new("valuePath", "Value path", Help: "e.g. stats.open or $count:items"),
        new("textPath", "Text path", Help: "Optional."),
        new("headerName", "Header name", Help: "Optional, e.g. Authorization."),
        new("headerValue", "Header value", SettingKind.Secret, Help: "env:VAR_NAME supported."),
    ];

    public async Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct)
    {
        var url = app.Get(this, "url");
        if (string.IsNullOrWhiteSpace(url)) return SourceReading.Fail("Set a URL.");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var header = app.Get(this, "headerName");
        if (!string.IsNullOrWhiteSpace(header)) req.Headers.TryAddWithoutValidation(header, app.Get(this, "headerValue"));

        using var resp = await http.CreateClient(nameof(HttpJsonSource)).SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return SourceReading.Fail($"HTTP {(int)resp.StatusCode}");
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));

        return new SourceReading
        {
            Value = JsonPath.Number(json, app.Get(this, "valuePath")),
            Text = JsonPath.Text(json, app.Get(this, "textPath")),
        };
    }
}

/// <summary>Runs a command; stdout is either a number, "number text", or JSON {value,text}.</summary>
public sealed class CommandSource : IDataSource
{
    public const string TypeName = "command";
    public string Type => TypeName;
    public string DisplayName => "Run a command";
    public string Description => "Runs a program each poll. Output: a number, \"<number> <text>\", or JSON {\"value\":1,\"text\":\"...\"}.";

    public IReadOnlyList<SettingField> Settings { get; } =
    [
        new("fileName", "Program", Help: "e.g. dotnet, pwsh, /usr/local/bin/mytool"),
        new("arguments", "Arguments", SettingKind.Multiline, Help: "One argument per line."),
        new("timeoutSeconds", "Timeout (s)", SettingKind.Number, "20"),
    ];

    public async Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct)
    {
        var file = app.Get(this, "fileName");
        if (string.IsNullOrWhiteSpace(file)) return SourceReading.Fail("Set a program.");
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in app.Get(this, "arguments").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            psi.ArgumentList.Add(arg);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(int.TryParse(app.Get(this, "timeoutSeconds"), out var s) ? s : 20));
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start process.");
        try
        {
            var stdout = await p.StandardOutput.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            return p.ExitCode == 0 ? ParseOutput(stdout) : SourceReading.Fail($"Exit code {p.ExitCode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            p.Kill(entireProcessTree: true);
            return SourceReading.Fail("Timed out.");
        }
    }

    public static SourceReading ParseOutput(string stdout)
    {
        var trimmed = stdout.Trim();
        if (trimmed.StartsWith('{'))
        {
            var json = JsonNode.Parse(trimmed);
            return new SourceReading { Value = JsonPath.Number(json, "value"), Text = JsonPath.Text(json, "text") };
        }
        var m = Regex.Match(trimmed, @"^(-?\d+(?:\.\d+)?)\s*(.*)$", RegexOptions.Singleline);
        return m.Success
            ? new SourceReading { Value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Text = m.Groups[2].Value.Trim() }
            : new SourceReading { Text = trimmed };
    }
}

public static partial class JsonPath
{
    public static JsonNode? Select(JsonNode? node, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return node;
        foreach (Match seg in Segment().Matches(path))
        {
            if (node is null) return null;
            node = seg.Groups["idx"].Success
                ? node is JsonArray a && int.Parse(seg.Groups["idx"].Value) is var i && i < a.Count ? a[i] : null
                : node is JsonObject o ? o[seg.Groups["name"].Value] : null;
        }
        return node;
    }

    public static double? Number(JsonNode? root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("$count:", StringComparison.Ordinal))
            return Select(root, path[7..]) is JsonArray arr ? arr.Count : null;
        return Select(root, path) is JsonValue v
            ? v.TryGetValue<double>(out var d) ? d
              : v.TryGetValue<string>(out var str) && double.TryParse(str, CultureInfo.InvariantCulture, out d) ? d : null
            : null;
    }

    public static string? Text(JsonNode? root, string path) =>
        string.IsNullOrWhiteSpace(path) ? null : Select(root, path) switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            { } n => n.ToJsonString(),
            null => null,
        };

    [GeneratedRegex(@"(?<name>[^.\[\]]+)|\[(?<idx>\d+)\]")]
    private static partial Regex Segment();
}
