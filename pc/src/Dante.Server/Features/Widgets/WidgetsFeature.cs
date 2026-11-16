namespace Dante.Server.Features.Widgets;

/// <summary>The widgets on DANTE: which are active, their order, and the editor for one widget.</summary>
public sealed class WidgetsFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Widgets", "widgets", 10)];
}
