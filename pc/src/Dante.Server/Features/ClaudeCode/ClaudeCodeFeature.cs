using System.Text.Json.Nodes;
using Dante.Core.Engine;
using Dante.Core.Sources;

namespace Dante.Server.Features.ClaudeCode;

/// <summary>Claude Code: usage gauge, "a session is waiting on you" (via hooks) and the hook installer page.</summary>
public sealed class ClaudeCodeFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Claude Code", "claude-code", 60)];

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<ClaudeCodeSessionTracker>();
        services.AddSingleton<IDataSource, ClaudeCodeUsageSource>();
        services.AddSingleton<IDataSource, ClaudeCodeWaitingSource>();
    }

    // Claude Code hooks pipe their event JSON here (see the Claude Code page).
    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(ClaudeHookInstaller.Marker, (JsonObject payload, ClaudeCodeSessionTracker tracker) =>
        {
            tracker.Handle(payload);
            return Results.Ok();
        });

    // A hook event updates DANTE immediately instead of at the next poll.
    public void Start(IServiceProvider services)
    {
        var engine = services.GetRequiredService<DanteEngine>();
        services.GetRequiredService<ClaudeCodeSessionTracker>().Changed +=
            () => engine.PollSourceType(ClaudeCodeWaitingSource.TypeName);
    }
}
