using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.DaysUntil;

/// <summary>
/// The folder's entry point. It is found automatically (no need to edit Program.cs or the menu): it just
/// registers the plugin and its widget types.
/// </summary>
public sealed class DaysUntilFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<IPlugin, DaysUntilPlugin>();
        services.AddSingleton<IWidgetType, DaysUntilWidget>();
    }
}
