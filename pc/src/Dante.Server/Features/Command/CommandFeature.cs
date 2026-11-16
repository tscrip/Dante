using Dante.Core.Sources;

namespace Dante.Server.Features.Command;

/// <summary>The output of a program as an app on DANTE.</summary>
public sealed class CommandFeature : IFeature
{
    public void AddServices(IServiceCollection services) =>
        services.AddSingleton<IDataSource, CommandSource>();
}
