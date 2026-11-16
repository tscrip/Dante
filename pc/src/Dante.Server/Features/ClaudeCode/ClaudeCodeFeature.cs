using System.Text.Json.Nodes;
using Dante.Core.Config;
using Dante.Core.Engine;
using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.ClaudeCode;

/// <summary>Claude Code: usage gauge, "a session is waiting on you" (via hooks) and the hook installer.</summary>
public sealed class ClaudeCodeFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<ClaudeCodePlugin>();
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<ClaudeCodePlugin>());
        services.AddSingleton<ClaudeCodeSessionTracker>();
        services.AddSingleton<IWidgetType, ClaudeCodeUsageSource>();
        services.AddSingleton<IWidgetType, ClaudeCodeWaitingSource>();
    }

    // Claude Code hooks pipe their event JSON here (see the plugin page).
    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(ClaudeHookInstaller.Marker, (JsonObject payload, ClaudeCodeSessionTracker tracker) =>
        {
            tracker.Handle(payload);
            return Results.Ok();
        });

    public void Start(IServiceProvider services)
    {
        PluginMigration.MoveToPlugin(services.GetRequiredService<ConfigStore>(), ClaudeCodePlugin.PluginId,
            ClaudeCodeUsageSource.TypeName, "credentialsPath", "token");
        // A hook event updates DANTE immediately instead of at the next poll.
        var engine = services.GetRequiredService<DanteEngine>();
        services.GetRequiredService<ClaudeCodeSessionTracker>().Changed +=
            () => engine.PollWidgetType(ClaudeCodeWaitingSource.TypeName);
    }
}
