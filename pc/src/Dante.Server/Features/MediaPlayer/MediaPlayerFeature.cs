using Dante.Core.Engine;
using Dante.Core.Media;
using Dante.Core.Os;
using Dante.Core.Plugins;
using Dante.Core.Widgets;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>The media player remote: what's playing on the PC, with controls on DANTE.</summary>
public sealed class MediaPlayerFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<MediaPlayerPlugin>();
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<MediaPlayerPlugin>());
        // Shared with the Magic button: TryAdd, so whichever feature registers first wins.
        services.TryAddSingleton<ICommandRunner, ProcessCommandRunner>();
        services.TryAddSingleton(sp => MediaBackends.ForThisOs(sp.GetRequiredService<ICommandRunner>()));
        services.AddSingleton(sp => NowPlayingSources.ForThisOs(
            sp.GetRequiredService<ICommandRunner>(), sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<MediaPlayerService>();
        services.AddSingleton<IWidgetType, MediaPlayerWidget>();
    }

    public void Start(IServiceProvider services) =>
        services.GetRequiredService<MediaPlayerService>().Initialize(services.GetRequiredService<DanteEngine>());
}
