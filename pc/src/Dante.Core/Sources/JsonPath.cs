using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Dante.Core.Sources;

/// <summary>Tiny dotted-path reader for JSON: "data.items[0].count", or "$count:items" to count an array.</summary>
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
