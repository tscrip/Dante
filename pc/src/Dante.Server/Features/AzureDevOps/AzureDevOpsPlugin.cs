using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.AzureDevOps;

/// <summary>Azure DevOps: pull requests waiting on you.</summary>
public sealed class AzureDevOpsPlugin : IPlugin
{
    public const string PluginId = "azure-devops";
    public string Id => PluginId;
    public string Name => "Azure DevOps";
    public string Description => "Counts the pull requests that are waiting on you: ones to review, or your own with feedback to address.";
    public int Order => 20;

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("organization", "Organization URL", SettingKind.Text, "https://dev.azure.com/your-org"),
        new("pat", "Personal access token", SettingKind.Secret, "env:DANTE_ADO_PAT",
            "Needs Code (Read). Write env:VAR_NAME to keep it in an environment variable instead of config.json."),
    ];
}
