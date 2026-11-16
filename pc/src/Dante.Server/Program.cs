using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;
using Dante.Core.Sources;
using Dante.Server.Components;
using Dante.Server.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient();

// ---- Kernel (Dante.Core): config, the link to DANTE, and the engine every feature plugs into ----------
var configPath = builder.Configuration["Dante:ConfigPath"] ?? ConfigStore.DefaultPath();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new ConfigStore(configPath));
builder.Services.AddSingleton<IUrlOpener, ShellUrlOpener>();
builder.Services.AddSingleton(sp => new DeviceLink(
    sp.GetRequiredService<ConfigStore>(),
    sp.GetRequiredService<ILogger<DeviceLink>>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceLink>());
builder.Services.AddSingleton<DeviceAlerts>();
builder.Services.AddSingleton<SourceRegistry>();
builder.Services.AddSingleton<DanteEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DanteEngine>());

// ---- Features: every vertical slice under Features/<Name>/ registers itself -----------------------------
builder.Services.AddFeatures();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// REST endpoints (localhost only) and event wiring live in the feature slices.
app.UseFeatures();

if (app.Configuration.GetValue("Dante:OpenBrowserOnStart", true) && !args.Contains("--no-browser"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://127.0.0.1:5280";
        try { app.Services.GetRequiredService<IUrlOpener>().Open(url); }
        catch { /* headless machine - just print the URL */ }
        app.Logger.LogInformation("DANTE config UI: {Url}", url);
    });
}

app.Run();

/// <summary>Lets integration tests start the app with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
