using System.Text.Json.Nodes;
using DeskBuddy.Core.ClaudeCode;
using DeskBuddy.Core.Config;
using DeskBuddy.Core.Device;
using DeskBuddy.Core.Engine;
using DeskBuddy.Core.Sources;
using DeskBuddy.Server.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient();

var configPath = builder.Configuration["DeskBuddy:ConfigPath"] ?? ConfigStore.DefaultPath();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new ConfigStore(configPath));
builder.Services.AddSingleton<PushStore>();
builder.Services.AddSingleton<ClaudeCodeSessionTracker>();
builder.Services.AddSingleton<IUrlOpener, ShellUrlOpener>();

// Data sources: add a class implementing IDataSource and register it here to get a new kind of app.
builder.Services.AddSingleton<IDataSource, ClaudeCodeUsageSource>();
builder.Services.AddSingleton<IDataSource, ClaudeCodeWaitingSource>();
builder.Services.AddSingleton<IDataSource, AzureDevOpsPullRequestSource>();
builder.Services.AddSingleton<IDataSource, HttpJsonSource>();
builder.Services.AddSingleton<IDataSource, CommandSource>();
builder.Services.AddSingleton<IDataSource, PushSource>();
builder.Services.AddSingleton<SourceRegistry>();

builder.Services.AddSingleton(sp => new DeviceLink(
    sp.GetRequiredService<ConfigStore>(),
    sp.GetRequiredService<ILogger<DeviceLink>>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceLink>());
builder.Services.AddSingleton<BuddyEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BuddyEngine>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// ---- REST API (localhost only) -------------------------------------------------------------

// Claude Code hooks pipe their event JSON here (see the Claude Code page in the UI).
app.MapPost(ClaudeHookInstaller.Marker, (JsonObject payload, ClaudeCodeSessionTracker tracker) =>
{
    tracker.Handle(payload);
    return Results.Ok();
});

// Push a value into an app whose source is "push".
app.MapPost("/api/apps/{id}/value", (string id, PushRequest body, PushStore store) =>
{
    store.Set(id, new SourceReading
    {
        Value = body.Value,
        Text = body.Text,
        Pending = body.Pending?.Select(p => new PendingItem(p.Key, p.Title ?? p.Key, p.Since ?? DateTimeOffset.UtcNow, p.Url)).ToList(),
    });
    return Results.Accepted();
});

app.MapPost("/api/say", (SayRequest body, BuddyEngine engine) =>
{
    engine.Say(body.Text);
    return Results.Accepted();
});

app.MapGet("/api/state", (BuddyEngine engine, DeviceLink link) => new
{
    device = new { status = link.Status.ToString(), online = link.Online, port = link.PortName, firmware = link.Firmware },
    mood = new { mood = engine.CurrentMood.Mood.ToString(), pending = engine.CurrentMood.PendingCount, oldestMinutes = (int)engine.CurrentMood.OldestWait.TotalMinutes },
    apps = engine.Snapshot().Select(a => new { a.Definition.Id, a.Definition.Name, a.Reading?.Value, a.Reading?.Text, a.Level, a.AlertRaised, a.Error }),
});

if (app.Configuration.GetValue("DeskBuddy:OpenBrowserOnStart", true) && !args.Contains("--no-browser"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://127.0.0.1:5280";
        try { app.Services.GetRequiredService<IUrlOpener>().Open(url); }
        catch { /* headless machine - just print the URL */ }
        app.Logger.LogInformation("DeskBuddy config UI: {Url}", url);
    });
}

app.Run();

public sealed record PushRequest(double? Value, string? Text, List<PushPendingItem>? Pending);
public sealed record PushPendingItem(string Key, string? Title, DateTimeOffset? Since, string? Url);
public sealed record SayRequest(string Text);
