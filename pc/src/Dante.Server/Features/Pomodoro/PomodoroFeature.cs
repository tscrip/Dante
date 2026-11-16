using Dante.Core.Engine;
using Dante.Core.Sources;

namespace Dante.Server.Features.Pomodoro;

/// <summary>A Pomodoro timer on DANTE, started with the MAGIC button and configured on the Pomodoro page.</summary>
public sealed class PomodoroFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Pomodoro", "pomodoro", 40)];

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<PomodoroService>();
        services.AddHostedService(sp => sp.GetRequiredService<PomodoroService>());
        services.AddSingleton<IDataSource, PomodoroSource>();
    }

    public void Start(IServiceProvider services) => services.GetRequiredService<PomodoroService>().Initialize(services.GetRequiredService<DanteEngine>());
}
