using Dante.Core.Engine;
using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.Pomodoro;

/// <summary>A Pomodoro timer on DANTE, started with the MAGIC button and configured on its plugin page.</summary>
public sealed class PomodoroFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<IPlugin, PomodoroPlugin>();
        services.AddSingleton<PomodoroService>();
        services.AddHostedService(sp => sp.GetRequiredService<PomodoroService>());
        services.AddSingleton<IWidgetType, PomodoroSource>();
    }

    public void Start(IServiceProvider services) => services.GetRequiredService<PomodoroService>().Initialize(services.GetRequiredService<DanteEngine>());
}
