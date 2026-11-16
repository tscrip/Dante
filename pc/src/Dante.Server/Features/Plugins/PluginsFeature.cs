namespace Dante.Server.Features.Plugins;

/// <summary>The plugin list and each plugin's page (its settings, its own panel and its widgets).</summary>
public sealed class PluginsFeature : IFeature
{
    /// <summary>The sidebar lists every plugin under this entry.</summary>
    public const string Href = "plugins";

    public IEnumerable<NavItem> Navigation => [new("Plugins", Href, 15)];
}
