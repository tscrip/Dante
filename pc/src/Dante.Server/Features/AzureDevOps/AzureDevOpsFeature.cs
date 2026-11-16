using Dante.Core.Config;
using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.AzureDevOps;

/// <summary>Pull requests waiting on you in Azure DevOps.</summary>
public sealed class AzureDevOpsFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<AzureDevOpsPlugin>();
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<AzureDevOpsPlugin>());
        services.AddSingleton<IWidgetType, AzureDevOpsPullRequestSource>();
    }

    public void Start(IServiceProvider services) =>
        PluginMigration.MoveToPlugin(services.GetRequiredService<ConfigStore>(), AzureDevOpsPlugin.PluginId,
            AzureDevOpsPullRequestSource.TypeName, "organization", "pat");
}
