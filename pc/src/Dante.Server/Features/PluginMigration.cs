using Dante.Core.Config;
using Dante.Core.Plugins;

namespace Dante.Server.Features;

public static class PluginMigration
{
    /// <summary>
    /// Before plugins had their own settings, shared values (an organisation, a token) were stored on each
    /// widget. Moves them to the plugin once: the first widget's non-empty value wins, and the keys are
    /// removed from the widgets that had that value (or none). Does nothing once the plugin has settings.
    /// </summary>
    public static void MoveToPlugin(ConfigStore store, string pluginId, string widgetType, params string[] keys)
    {
        var cfg = store.Snapshot();
        var key = PluginSettings.KeyFor(pluginId);
        if (cfg.Features.ContainsKey(key)) return;
        var widgets = cfg.Widgets.Where(w => w.Type.Equals(widgetType, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!widgets.Any(w => keys.Any(w.Settings.ContainsKey))) return;

        var values = new Dictionary<string, string>();
        foreach (var k in keys)
        {
            var value = widgets.Select(w => w.Settings.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            if (value is not null) values[k] = value;
            // A widget with a different value keeps it: widget values still win over the plugin's.
            foreach (var w in widgets.Where(w => string.IsNullOrEmpty(w.Settings.GetValueOrDefault(k)) || w.Settings[k] == value))
                w.Settings.Remove(k);
        }
        cfg.SetFeature(key, values);
        store.Save(cfg);
    }
}
