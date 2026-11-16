using Dante.Core.Engine;

namespace Dante.Server.Features.MagicButton;

/// <summary>The MAGIC button on top of DANTE: media keys, mute, open a URL or run a command on the PC.</summary>
public sealed class MagicButtonFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Magic button", "magic", 30)];

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
        services.AddSingleton(sp => PcActions.BackendForThisOs(sp.GetRequiredService<ICommandRunner>()));
        services.AddSingleton<IPcActions, PcActions>();
        services.AddSingleton<MagicButtonService>();
    }

    public void Start(IServiceProvider services) =>
        services.GetRequiredService<DanteEngine>().AddHandler(services.GetRequiredService<MagicButtonService>(), MagicButtonService.HandlerOrder);
}
