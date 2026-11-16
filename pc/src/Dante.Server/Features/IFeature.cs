using System.Reflection;

namespace Dante.Server.Features;

/// <summary>An entry in the sidebar. Lower <see cref="Order"/> comes first.</summary>
public sealed record NavItem(string Title, string Href, int Order = 500);

/// <summary>
/// One vertical slice of DANTE: everything a feature needs lives in its own folder under
/// <c>Features/&lt;Name&gt;/</c> (page, endpoints, data sources, services, settings), and this class is the
/// slice's only entry point. Features are found by reflection, so adding a folder with a class that
/// implements <see cref="IFeature"/> is all it takes: no edits to Program.cs, the layout or the kernel.
/// </summary>
/// <remarks>
/// The kernel (Dante.Core) offers the extension points: <see cref="Dante.Core.Sources.IDataSource"/> for a new
/// kind of app screen, <see cref="Dante.Core.Engine.DanteEngine.AddHandler"/> to react to DANTE's buttons,
/// <see cref="Dante.Core.Device.DeviceAlerts"/> to nudge the human, and
/// <see cref="Dante.Core.Config.DanteConfig.Feature{T}"/> for the feature's own settings.
/// </remarks>
public interface IFeature
{
    /// <summary>Sidebar links to the feature's pages.</summary>
    IEnumerable<NavItem> Navigation => [];

    /// <summary>Registers the feature's services (data sources, background services, ...).</summary>
    void AddServices(IServiceCollection services) { }

    /// <summary>Maps the feature's REST endpoints.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints) { }

    /// <summary>Runs once the app is built: subscribe to events, register device button handlers.</summary>
    void Start(IServiceProvider services) { }
}

/// <summary>All features in this assembly, in a stable order.</summary>
public sealed class FeatureCatalog(IReadOnlyList<IFeature> features)
{
    public IReadOnlyList<IFeature> Features { get; } = features;

    public IReadOnlyList<NavItem> Navigation { get; } =
        [.. features.SelectMany(f => f.Navigation).OrderBy(n => n.Order).ThenBy(n => n.Title, StringComparer.Ordinal)];

    public static FeatureCatalog Discover(Assembly assembly) => new(
    [
        .. assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFeature).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IFeature)Activator.CreateInstance(t)!),
    ]);
}

public static class FeatureExtensions
{
    /// <summary>Finds every feature slice and lets it register its services.</summary>
    public static FeatureCatalog AddFeatures(this IServiceCollection services)
    {
        var catalog = FeatureCatalog.Discover(typeof(IFeature).Assembly);
        services.AddSingleton(catalog);
        foreach (var feature in catalog.Features) feature.AddServices(services);
        return catalog;
    }

    /// <summary>Maps every feature's endpoints and starts the features.</summary>
    public static WebApplication UseFeatures(this WebApplication app)
    {
        var catalog = app.Services.GetRequiredService<FeatureCatalog>();
        foreach (var feature in catalog.Features) feature.MapEndpoints(app);
        foreach (var feature in catalog.Features) feature.Start(app.Services);
        return app;
    }
}
