using Dante.Core.Engine;
using Dante.Core.Sources;

namespace Dante.Server.Features.Push;

/// <summary>Apps whose values are pushed in over REST: POST /api/apps/{id}/value.</summary>
public sealed class PushFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<PushStore>();
        services.AddSingleton<IDataSource, PushSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/apps/{id}/value", (string id, PushRequest body, PushStore store) =>
        {
            store.Set(id, new SourceReading
            {
                Value = body.Value,
                Text = body.Text,
                Pending = body.Pending?.Select(p => new PendingItem(p.Key, p.Title ?? p.Key, p.Since ?? DateTimeOffset.UtcNow, p.Url)).ToList(),
            });
            return Results.Accepted();
        });

    // A push shows up on DANTE straight away instead of at the next poll.
    public void Start(IServiceProvider services)
    {
        var engine = services.GetRequiredService<DanteEngine>();
        services.GetRequiredService<PushStore>().Pushed += engine.PollNow;
    }
}

public sealed record PushRequest(double? Value, string? Text, List<PushPendingItem>? Pending);
public sealed record PushPendingItem(string Key, string? Title, DateTimeOffset? Since, string? Url);
