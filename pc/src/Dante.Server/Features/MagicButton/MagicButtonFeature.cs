using Dante.Core.Engine;
using Dante.Core.Media;
using Dante.Core.Os;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dante.Server.Features.MagicButton;

/// <summary>The MAGIC button on top of DANTE: media keys, mute, open a URL or run a command on the PC.</summary>
public sealed class MagicButtonFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Magic button", "magic", 30)];

    public void AddServices(IServiceCollection services)
    {
        // Shared with the media player plugin: TryAdd, so whichever feature registers first wins.
        services.TryAddSingleton<ICommandRunner, ProcessCommandRunner>();
        services.TryAddSingleton(sp => MediaBackends.ForThisOs(sp.GetRequiredService<ICommandRunner>()));
        services.AddSingleton<IPcActions, PcActions>();
        services.AddSingleton<MagicButtonService>();
    }

    public void Start(IServiceProvider services) =>
        services.GetRequiredService<DanteEngine>().AddHandler(services.GetRequiredService<MagicButtonService>(), MagicButtonService.HandlerOrder);
}
