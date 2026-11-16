using Dante.Core.Sources;

namespace Dante.Server.Features.AzureDevOps;

/// <summary>Pull requests waiting on you in Azure DevOps, as an app on DANTE.</summary>
public sealed class AzureDevOpsFeature : IFeature
{
    public void AddServices(IServiceCollection services) =>
        services.AddSingleton<IDataSource, AzureDevOpsPullRequestSource>();
}
