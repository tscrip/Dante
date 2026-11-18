using Dante.Core.Plugins;

namespace Dante.Server.Features.SystemPerf;

/// <summary>CPU and memory use of this PC, on Windows, macOS and Linux alike.</summary>
public sealed class SystemPerfPlugin : IPlugin
{
    public const string PluginId = "system";
    public string Id => PluginId;
    public string Name => "System performance";
    public string Description =>
        "How busy this PC is: CPU and memory use as two live graphs on one screen. Works the same on Windows, macOS and " +
        "Linux; no commands to write. Set how often it samples in the widget (Check every).";
    public int Order => 50;
    public Type ConfigComponent => typeof(SystemPerfPanel);
}
