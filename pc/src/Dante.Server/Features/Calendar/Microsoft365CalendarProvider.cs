using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Dante.Core.Config;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Dante.Server.Features.Calendar;

/// <summary>Settings for the Outlook / Microsoft 365 calendar, stored under features["calendar.microsoft365"].</summary>
public sealed class Microsoft365Settings
{
    public const string Key = "calendar.microsoft365";

    /// <summary>Application (client) ID of your app registration in Microsoft Entra ID.</summary>
    public string ClientId { get; set; } = "";
    /// <summary>"common" (work, school and personal accounts), "organizations", "consumers", or a tenant ID / domain.</summary>
    public string Tenant { get; set; } = "common";
}

/// <summary>A device-code sign-in in progress: the human opens <see cref="Url"/> and types <see cref="Code"/>.</summary>
public sealed record DeviceCodePrompt(string Url, string Code, string Message, DateTimeOffset ExpiresAt);

/// <summary>
/// Outlook / Microsoft 365 calendars through Microsoft Graph (<c>/me/calendarView</c>, delegated
/// <c>Calendars.Read</c>). Sign-in uses the OAuth device code flow: no redirect URI or client secret, which
/// suits an app that runs on localhost. Tokens are cached with MSAL's cross-platform cache (DPAPI on
/// Windows, Keychain on macOS, the keyring on Linux, or a user-only file when no keyring is available).
/// </summary>
public sealed class Microsoft365CalendarProvider(ConfigStore config, IHttpClientFactory http, ILogger<Microsoft365CalendarProvider> log)
    : ICalendarProvider
{
    public const string ProviderId = "microsoft365";
    public static readonly string[] Scopes = ["Calendars.Read"];
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPublicClientApplication? _app;
    private string? _appKey;
    private IAccount? _account;

    public string Id => ProviderId;
    public string DisplayName => "Outlook / Microsoft 365";
    public Type SetupComponent => typeof(Microsoft365Setup);
    public bool IsReady => _account is not null;
    public string? AccountName => _account?.Username;

    /// <summary>Set while a device-code sign-in is waiting for the human.</summary>
    public DeviceCodePrompt? PendingSignIn { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;

    public Microsoft365Settings Settings => config.Snapshot().Feature<Microsoft365Settings>(Microsoft365Settings.Key);

    /// <summary>Loads the token cache and the signed-in account, if any. Safe to call repeatedly.</summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        var app = await AppAsync(ct);
        if (app is null) { _account = null; return; }
        _account = (await app.GetAccountsAsync()).FirstOrDefault();
        Changed?.Invoke();
    }

    /// <summary>
    /// Starts a device-code sign-in. Returns once the code is known (see <see cref="PendingSignIn"/>);
    /// the sign-in finishes in the background when the human completes it in the browser.
    /// </summary>
    public async Task<DeviceCodePrompt> BeginSignInAsync(CancellationToken ct = default)
    {
        var app = await AppAsync(ct) ?? throw new InvalidOperationException("Enter the Application (client) ID of your app registration first.");
        var prompt = new TaskCompletionSource<DeviceCodePrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
        LastError = null;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await app.AcquireTokenWithDeviceCode(Scopes, code =>
                {
                    PendingSignIn = new DeviceCodePrompt(code.VerificationUrl, code.UserCode, code.Message, code.ExpiresOn);
                    prompt.TrySetResult(PendingSignIn);
                    Changed?.Invoke();
                    return Task.CompletedTask;
                }).ExecuteAsync(CancellationToken.None);
                _account = result.Account;
            }
            catch (Exception ex)
            {
                LastError = ex is MsalException msal ? Explain(msal) : ex.Message;
                log.LogWarning(ex, "Microsoft 365 sign-in failed");
                prompt.TrySetException(ex);
            }
            finally
            {
                PendingSignIn = null;
                Changed?.Invoke();
            }
        }, CancellationToken.None);

        return await prompt.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }

    public async Task SignOutAsync()
    {
        var app = await AppAsync(default);
        if (app is not null)
            foreach (var account in await app.GetAccountsAsync()) await app.RemoveAsync(account);
        _account = null;
        Changed?.Invoke();
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var token = await TokenAsync(ct);
        var client = http.CreateClient(nameof(Microsoft365CalendarProvider));
        var url = $"{GraphBase}/me/calendarView?startDateTime={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}" +
                  $"&endDateTime={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}" +
                  "&$select=id,subject,start,end,isAllDay,location,onlineMeeting,onlineMeetingUrl,webLink,responseStatus,showAs,isCancelled" +
                  "&$orderby=start/dateTime&$top=100";
        var events = new List<CalendarEvent>();
        for (var page = 0; url is not null && page < 10; page++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");   // all times in UTC: no Windows time zone names to map
            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Microsoft Graph said {(int)resp.StatusCode}: {GraphError(body)}");
            var json = JsonNode.Parse(body);
            events.AddRange(ParseEvents(json));
            url = json?["@odata.nextLink"]?.GetValue<string>();
        }
        return events;
    }

    /// <summary>Turns a Graph <c>calendarView</c> page (times in UTC) into events.</summary>
    public static IEnumerable<CalendarEvent> ParseEvents(JsonNode? page)
    {
        if (page?["value"] is not JsonArray items) yield break;
        foreach (var item in items.OfType<JsonObject>())
        {
            if (Time(item["start"]) is not { } start || Time(item["end"]) is not { } end) continue;
            yield return new CalendarEvent(
                Id: item["id"]?.GetValue<string>() ?? "",
                Title: item["subject"]?.GetValue<string>() ?? "(no title)",
                Start: start,
                End: end,
                IsAllDay: item["isAllDay"]?.GetValue<bool>() ?? false,
                Location: NullIfEmpty(item["location"]?["displayName"]?.GetValue<string>()),
                JoinUrl: NullIfEmpty(item["onlineMeeting"]?["joinUrl"]?.GetValue<string>()) ?? NullIfEmpty(item["onlineMeetingUrl"]?.GetValue<string>()),
                WebLink: NullIfEmpty(item["webLink"]?.GetValue<string>()),
                Response: (item["responseStatus"]?["response"]?.GetValue<string>()) switch
                {
                    "organizer" => ResponseStatus.Organizer,
                    "accepted" => ResponseStatus.Accepted,
                    "tentativelyAccepted" => ResponseStatus.Tentative,
                    "declined" => ResponseStatus.Declined,
                    "notResponded" => ResponseStatus.NotResponded,
                    _ => ResponseStatus.None,
                },
                ShowAs: (item["showAs"]?.GetValue<string>()) switch
                {
                    "free" => ShowAs.Free,
                    "tentative" => ShowAs.Tentative,
                    "busy" => ShowAs.Busy,
                    "oof" => ShowAs.OutOfOffice,
                    "workingElsewhere" => ShowAs.WorkingElsewhere,
                    _ => ShowAs.Unknown,
                },
                IsCancelled: item["isCancelled"]?.GetValue<bool>() ?? false);
        }
    }

    // {"dateTime":"2026-11-16T09:00:00.0000000","timeZone":"UTC"}
    private static DateTimeOffset? Time(JsonNode? node)
    {
        var text = node?["dateTime"]?.GetValue<string>();
        if (text is null || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
            return null;
        var zone = node?["timeZone"]?.GetValue<string>();
        if (zone is not null && zone != "UTC" && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var tz))
            return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), tz.GetUtcOffset(utc));
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string GraphError(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? body; }
        catch (System.Text.Json.JsonException) { return body.Length > 200 ? body[..200] : body; }
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        var app = await AppAsync(ct) ?? throw new InvalidOperationException("Outlook isn't set up: enter the client ID and sign in on the Calendar page.");
        _account ??= (await app.GetAccountsAsync()).FirstOrDefault();
        if (_account is null) throw new InvalidOperationException("Not signed in to Outlook: sign in on the Calendar page.");
        try
        {
            var result = await app.AcquireTokenSilent(Scopes, _account).ExecuteAsync(ct);
            LastError = null;
            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            LastError = "Your Microsoft sign-in expired or needs consent again: sign in on the Calendar page.";
            log.LogWarning(ex, "Microsoft 365 needs an interactive sign-in");
            throw new InvalidOperationException(LastError, ex);
        }
    }

    /// <summary>The MSAL app for the current client ID / tenant (rebuilt when they change), with a persistent token cache.</summary>
    private async Task<IPublicClientApplication?> AppAsync(CancellationToken ct)
    {
        var s = Settings;
        if (string.IsNullOrWhiteSpace(s.ClientId)) return null;
        var key = $"{s.ClientId.Trim()}|{s.Tenant.Trim()}";
        await _gate.WaitAsync(ct);
        try
        {
            if (_app is not null && _appKey == key) return _app;
            var app = PublicClientApplicationBuilder.Create(s.ClientId.Trim())
                .WithAuthority($"https://login.microsoftonline.com/{(string.IsNullOrWhiteSpace(s.Tenant) ? "common" : s.Tenant.Trim())}")
                .WithDefaultRedirectUri()
                .Build();
            await AttachCacheAsync(app);
            _app = app;
            _appKey = key;
            _account = (await app.GetAccountsAsync()).FirstOrDefault();
            return app;
        }
        finally { _gate.Release(); }
    }

    private async Task AttachCacheAsync(IPublicClientApplication app)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(config.FilePath))!;
        const string file = "msal-calendar.cache";
        var secure = new StorageCreationPropertiesBuilder(file, dir)
            .WithMacKeyChain("DANTE", "DANTE calendar")
            .WithLinuxKeyring("dante.calendar.tokens", MsalCacheHelper.LinuxKeyRingDefaultCollection, "DANTE calendar sign-in",
                new KeyValuePair<string, string>("Version", "1"), new KeyValuePair<string, string>("ProductGroup", "DANTE"))
            .Build();
        try
        {
            var helper = await MsalCacheHelper.CreateAsync(secure);
            helper.VerifyPersistence();
            helper.RegisterCache(app.UserTokenCache);
        }
        catch (MsalCachePersistenceException ex)
        {
            // No keyring (e.g. a headless Linux box): fall back to a file only this user can read.
            log.LogWarning(ex, "No OS keyring for the calendar sign-in; caching it in a user-only file in {Dir}", dir);
            var plain = new StorageCreationPropertiesBuilder(file, dir).WithUnprotectedFile().Build();
            var helper = await MsalCacheHelper.CreateAsync(plain);
            helper.RegisterCache(app.UserTokenCache);
        }
    }

    private static string Explain(MsalException ex) => ex.ErrorCode switch
    {
        "authorization_declined" or "access_denied" => "Sign-in was cancelled or declined.",
        "code_expired" or "expired_token" => "The code expired before sign-in finished. Try again.",
        "invalid_client" or "unauthorized_client" =>
            "Microsoft rejected the client ID. Check it, and that \"Allow public client flows\" is on in the app registration.",
        _ => ex.Message,
    };
}
