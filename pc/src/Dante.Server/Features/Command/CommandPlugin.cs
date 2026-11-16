using Dante.Core.Plugins;

namespace Dante.Server.Features.Command;

/// <summary>The output of a program or script.</summary>
public sealed class CommandPlugin : IPlugin
{
    public const string PluginId = "command";
    public string Id => PluginId;
    public string Name => "Command";
    public string Description => "Runs a program or script on this PC and shows what it prints. Add one widget per command.";
    public int Order => 85;
}
