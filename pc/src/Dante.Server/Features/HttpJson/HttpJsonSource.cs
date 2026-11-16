using System.Text.Json.Nodes;
using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.HttpJson;

/// <summary>Polls any JSON endpoint and picks fields with a dotted path (e.g. "data.items[0].count").</summary>
public sealed class HttpJsonSource(IHttpClientFactory http) : IWidgetType
{
    public const string TypeName = "http-json";
    public string Type => TypeName;
    public string PluginId => HttpJsonPlugin.PluginId;
    public string DisplayName => "JSON value";
    public string Description => "GETs a URL and reads a number (and optional text) using dotted paths like data.items[0].count. Use \"$count:path\" to count an array.";

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("url", "URL"),
        new("valuePath", "Value path", Help: "e.g. stats.open or $count:items"),
        new("textPath", "Text path", Help: "Optional."),
        new("headerName", "Header name", Help: "Optional, e.g. Authorization."),
        new("headerValue", "Header value", SettingKind.Secret, Help: "env:VAR_NAME supported."),
    ];

    public async Task<WidgetReading> ReadAsync(WidgetDefinition app, CancellationToken ct)
    {
        var url = app.Get(this, "url");
        if (string.IsNullOrWhiteSpace(url)) return WidgetReading.Fail("Set a URL.");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var header = app.Get(this, "headerName");
        if (!string.IsNullOrWhiteSpace(header)) req.Headers.TryAddWithoutValidation(header, app.Get(this, "headerValue"));

        using var resp = await http.CreateClient(nameof(HttpJsonSource)).SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return WidgetReading.Fail($"HTTP {(int)resp.StatusCode}");
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));

        return new WidgetReading
        {
            Value = JsonPath.Number(json, app.Get(this, "valuePath")),
            Text = JsonPath.Text(json, app.Get(this, "textPath")),
        };
    }
}

