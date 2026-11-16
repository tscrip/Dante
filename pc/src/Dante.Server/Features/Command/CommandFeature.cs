using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.Command;

/// <summary>The output of a program as an app on DANTE.</summary>
public sealed class CommandFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<IPlugin, CommandPlugin>();
        services.AddSingleton<IWidgetType, CommandSource>();
    }
}
