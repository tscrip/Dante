using Dante.Core.Device;
using Dante.Core.Engine;

namespace Dante.Server.Features.Dashboard;

/// <summary>The home page, plus the small REST API for scripts: say something, read the state.</summary>
public sealed class DashboardFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Dashboard", "", 0)];

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/say", (SayRequest body, DanteEngine engine) =>
        {
            engine.Say(body.Text);
            return Results.Accepted();
        });

        endpoints.MapGet("/api/state", (DanteEngine engine, DeviceLink link, DeviceAlerts alerts) => new
        {
            device = new { status = link.Status.ToString(), online = link.Online, port = link.PortName, firmware = link.Firmware, display = link.DeviceDisplay },
            mood = new { mood = engine.CurrentMood.Mood.ToString(), pending = engine.CurrentMood.PendingCount, oldestMinutes = (int)engine.CurrentMood.OldestWait.TotalMinutes },
            widgets = engine.Snapshot().Select(a => new { a.Definition.Id, a.Definition.Name, a.Definition.Type, a.Reading?.Value, a.Reading?.Text, a.Level, a.AlertRaised, a.Error }),
            nudges = alerts.Active.Select(a => new { a.Id, a.Title, a.Text, a.Level }),
        });
    }
}

public sealed record SayRequest(string Text);
