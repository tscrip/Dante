using Dante.Core.Plugins;

namespace Dante.Server.Features.Push;

/// <summary>Values that scripts and other tools push to DANTE over REST.</summary>
public sealed class PushPlugin : IPlugin
{
    public const string PluginId = "push";
    public string Id => PluginId;
    public string Name => "REST push";
    public string Description =>
        "Lets any script or tool push a value to a widget: POST to http://127.0.0.1:5280/api/widgets/{widget id}/value. " +
        "Add one widget per thing you push; each widget's page shows the exact command.";
    public int Order => 90;
}
