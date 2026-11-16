using Dante.Core.Sources;

namespace Dante.Server.Features.HttpJson;

/// <summary>Any JSON endpoint as an app on DANTE.</summary>
public sealed class HttpJsonFeature : IFeature
{
    public void AddServices(IServiceCollection services) =>
        services.AddSingleton<IDataSource, HttpJsonSource>();
}
