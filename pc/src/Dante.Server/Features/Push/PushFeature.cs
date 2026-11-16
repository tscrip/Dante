using Dante.Core.Engine;
using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.Push;

/// <summary>Widgets whose values are pushed in over REST: POST /api/widgets/{id}/value.</summary>
public sealed class PushFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<IPlugin, PushPlugin>();
        services.AddSingleton<PushStore>();
        services.AddSingleton<IWidgetType, PushSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // "apps" is what widgets were called before; both URLs work.
        endpoints.MapPost("/api/widgets/{id}/value", Push);
        endpoints.MapPost("/api/apps/{id}/value", Push);
    }

    private static IResult Push(string id, PushRequest body, PushStore store)
    {
        store.Set(id, new WidgetReading
        {
            Value = body.Value,
            Text = body.Text,
            Pending = body.Pending?.Select(p => new PendingItem(p.Key, p.Title ?? p.Key, p.Since ?? DateTimeOffset.UtcNow, p.Url)).ToList(),
        });
        return Results.Accepted();
    }

    // A push shows up on DANTE straight away instead of at the next poll.
    public void Start(IServiceProvider services)
    {
        var engine = services.GetRequiredService<DanteEngine>();
        services.GetRequiredService<PushStore>().Pushed += engine.PollNow;
    }
}

public sealed record PushRequest(double? Value, string? Text, List<PushPendingItem>? Pending);
public sealed record PushPendingItem(string Key, string? Title, DateTimeOffset? Since, string? Url);
