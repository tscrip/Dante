using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Dante.Core.Config;

namespace Dante.Core.Sources;

/// <summary>
/// Polls Azure DevOps for active pull requests that need you: ones where you're a reviewer and
/// haven't voted yet ("review"), ones you authored that reviewers are waiting on you for
/// ("mine" = waiting-for-author votes), or both.
/// </summary>
public sealed class AzureDevOpsPullRequestSource(IHttpClientFactory http, TimeProvider time) : IDataSource
{
    public const string TypeName = "azure-devops-prs";
    private const string ApiVersion = "7.1";

    // Org URL + PAT hash -> my identity id (connectionData is cheap but no need to call it every poll).
    private readonly ConcurrentDictionary<string, string> _identityCache = new();

    public string Type => TypeName;
    public string DisplayName => "Azure DevOps: pull requests";
    public string Description => "Counts active pull requests that need your attention. Needs a PAT with Code (Read) scope.";

    public IReadOnlyList<SettingField> Settings { get; } =
    [
        new("organization", "Organization URL", SettingKind.Text, "https://dev.azure.com/your-org"),
        new("project", "Project", SettingKind.Text, "", "Blank = every project in the organization."),
        new("pat", "Personal access token", SettingKind.Secret, "env:DANTE_ADO_PAT",
            "Prefer env:VAR_NAME so the token isn't stored in config.json."),
        new("mode", "Count", SettingKind.Choice, "review", Choices: ["review", "mine", "both"]),
        new("includeDrafts", "Include drafts", SettingKind.Bool, "false"),
    ];

    public async Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct)
    {
        var org = app.Get(this, "organization").TrimEnd('/');
        var project = app.Get(this, "project").Trim();
        var pat = app.Get(this, "pat");
        if (string.IsNullOrWhiteSpace(org) || org.Contains("your-org"))
            return SourceReading.Fail("Set the organization URL.");
        if (string.IsNullOrWhiteSpace(pat))
            return SourceReading.Fail("No PAT (is the environment variable set?).");

        var client = http.CreateClient(nameof(AzureDevOpsPullRequestSource));
        var auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat)));

        var me = await GetMyIdAsync(client, auth, org, pat, ct);
        if (me is null) return SourceReading.Fail("Could not resolve your identity - check the PAT.");

        var mode = app.Get(this, "mode");
        var includeDrafts = app.Get(this, "includeDrafts").Equals("true", StringComparison.OrdinalIgnoreCase);
        var basePath = string.IsNullOrEmpty(project) ? org : $"{org}/{Uri.EscapeDataString(project)}";

        var prs = new List<JsonObject>();
        if (mode is "review" or "both")
            prs.AddRange(await QueryAsync(client, auth, $"{basePath}/_apis/git/pullrequests?searchCriteria.status=active&searchCriteria.reviewerId={me}&$top=200&api-version={ApiVersion}", ct));
        if (mode is "mine" or "both")
            prs.AddRange(await QueryAsync(client, auth, $"{basePath}/_apis/git/pullrequests?searchCriteria.status=active&searchCriteria.creatorId={me}&$top=200&api-version={ApiVersion}", ct));

        return Evaluate(prs, me, mode, includeDrafts, org, time.GetUtcNow());
    }

    /// <summary>Pure filtering logic, separated for tests.</summary>
    public static SourceReading Evaluate(IEnumerable<JsonObject> prs, string me, string mode, bool includeDrafts, string org, DateTimeOffset now)
    {
        var items = new Dictionary<string, PendingItem>();
        foreach (var pr in prs)
        {
            var id = pr["pullRequestId"]?.GetValue<int>().ToString() ?? "";
            if (id.Length == 0 || items.ContainsKey(id)) continue;
            if (!includeDrafts && pr["isDraft"]?.GetValue<bool>() == true) continue;

            var creatorId = pr["createdBy"]?["id"]?.GetValue<string>();
            var reviewers = pr["reviewers"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
            var myVote = reviewers.FirstOrDefault(r => Same(r["id"], me))?["vote"]?.GetValue<int>();

            var needsMyReview = mode is "review" or "both" && !string.Equals(creatorId, me, StringComparison.OrdinalIgnoreCase) && myVote == 0;
            // -5 = "waiting for author", -10 = rejected: the ball is in my court as the author.
            var needsMyFix = mode is "mine" or "both" && string.Equals(creatorId, me, StringComparison.OrdinalIgnoreCase)
                             && reviewers.Any(r => r["vote"]?.GetValue<int>() is -5 or -10);
            if (!needsMyReview && !needsMyFix) continue;

            var repo = pr["repository"]?["name"]?.GetValue<string>() ?? "";
            var projectName = pr["repository"]?["project"]?["name"]?.GetValue<string>() ?? "";
            var title = pr["title"]?.GetValue<string>() ?? $"PR {id}";
            var created = DateTimeOffset.TryParse(pr["creationDate"]?.GetValue<string>(), out var c) ? c : now;
            var url = $"{org}/{Uri.EscapeDataString(projectName)}/_git/{Uri.EscapeDataString(repo)}/pullrequest/{id}";
            items[id] = new PendingItem($"pr{id}", $"!{id} {title}", created, url);
        }

        var pending = items.Values.OrderBy(i => i.Since).ToList();
        var text = pending.Count == 0 ? "inbox zero!" : $"oldest {Format.Age(now - pending[0].Since)}";
        return new SourceReading { Value = pending.Count, Text = text, Pending = pending };
    }

    private static bool Same(JsonNode? node, string id) =>
        string.Equals(node?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> GetMyIdAsync(HttpClient client, AuthenticationHeaderValue auth, string org, string pat, CancellationToken ct)
    {
        var cacheKey = org + "|" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(pat)));
        if (_identityCache.TryGetValue(cacheKey, out var cached)) return cached;

        var json = await GetJsonAsync(client, auth, $"{org}/_apis/connectionData?api-version=7.1-preview", ct);
        var id = json?["authenticatedUser"]?["id"]?.GetValue<string>();
        if (id is not null) _identityCache[cacheKey] = id;
        return id;
    }

    private static async Task<IEnumerable<JsonObject>> QueryAsync(HttpClient client, AuthenticationHeaderValue auth, string url, CancellationToken ct)
    {
        var json = await GetJsonAsync(client, auth, url, ct);
        return json?["value"]?.AsArray().OfType<JsonObject>() ?? [];
    }

    private static async Task<JsonObject?> GetJsonAsync(HttpClient client, AuthenticationHeaderValue auth, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = auth;
        using var resp = await client.SendAsync(req, ct);
        // A bad PAT gets a 203 + HTML sign-in page rather than a 401.
        if (!resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NonAuthoritativeInformation)
            throw new HttpRequestException($"Azure DevOps returned {(int)resp.StatusCode} for {new Uri(url).AbsolutePath}");
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct)) as JsonObject;
    }
}
