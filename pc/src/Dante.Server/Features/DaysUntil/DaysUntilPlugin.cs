using Dante.Core.Plugins;

namespace Dante.Server.Features.DaysUntil;

// THE EXAMPLE PLUGIN. It is deliberately small: copy this folder to start a new plugin.
// See CLAUDE.md ("Adding a plugin") for the step-by-step recipe.

/// <summary>
/// A plugin: shows up under Plugins in the sidebar, with one page at /plugins/days-until.
/// This one has no shared settings (each countdown widget has its own date), so its page only lists its widgets.
/// </summary>
public sealed class DaysUntilPlugin : IPlugin
{
    /// <summary>Stable id: used in the page URL and in config.json. Never change it once people use it.</summary>
    public const string PluginId = "days-until";

    public string Id => PluginId;
    public string Name => "Days until";
    public string Description =>
        "Counts down the days to things you're looking forward to (or dreading): a holiday, a launch, a birthday. " +
        "Add one widget per date.";
    public int Order => 70;
}
