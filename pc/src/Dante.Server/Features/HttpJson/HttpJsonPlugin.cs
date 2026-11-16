using Dante.Core.Plugins;

namespace Dante.Server.Features.HttpJson;

/// <summary>Any web API that answers in JSON.</summary>
public sealed class HttpJsonPlugin : IPlugin
{
    public const string PluginId = "web-api";
    public string Id => PluginId;
    public string Name => "Web API (JSON)";
    public string Description => "Polls any URL that answers in JSON and shows a number and text from it. Add one widget per URL.";
    public int Order => 80;
}
