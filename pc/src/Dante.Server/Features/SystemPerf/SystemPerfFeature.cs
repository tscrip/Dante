using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.SystemPerf;

/// <summary>A CPU and memory monitor widget.</summary>
public sealed class SystemPerfFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton(_ => ISystemStats.ForThisOs());
        services.AddSingleton<IPlugin, SystemPerfPlugin>();
        services.AddSingleton<IWidgetType, SystemPerfSource>();
    }
}
