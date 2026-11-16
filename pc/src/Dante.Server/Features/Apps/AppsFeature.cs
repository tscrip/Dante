namespace Dante.Server.Features.Apps;

/// <summary>The list of app screens on DANTE and the editor for one app.</summary>
public sealed class AppsFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Apps", "apps", 10)];
}
