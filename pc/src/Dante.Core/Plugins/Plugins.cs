using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Core.Plugins;

/// <summary>
/// A plugin adds something to DANTE (Claude Code, a calendar, a Pomodoro timer...). It has one
/// configuration page in the PC app with its shared <see cref="Fields"/> (and, if it needs more, its own
/// <see cref="ConfigComponent"/>), and offers widget types: the screens it can put on DANTE.
/// </summary>
public interface IPlugin
{
    /// <summary>Stable id, used in the page URL (/plugins/{id}) and in config.json.</summary>
    string Id { get; }
    string Name { get; }
    /// <summary>One or two sentences for the plugin list and the top of its page.</summary>
    string Description { get; }
    /// <summary>Shared settings, shown as a form on the plugin page (e.g. an organisation and a token).</summary>
    IReadOnlyList<SettingField> Fields => [];
    /// <summary>
    /// A Razor component with anything the form can't do (a sign-in, live status, a timer), shown on the
    /// plugin page. It is a server-side type; the kernel only passes it along.
    /// </summary>
    Type? ConfigComponent => null;
    /// <summary>Sort order in the sidebar (lower first; then by name).</summary>
    int Order => 100;
}

/// <summary>All plugins, with the widget types each offers.</summary>
public sealed class PluginRegistry(IEnumerable<IPlugin> plugins, WidgetTypeRegistry types)
{
    public IReadOnlyList<IPlugin> All { get; } =
        [.. plugins.OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    public IPlugin? Find(string id) => All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IWidgetType> WidgetTypes(string pluginId) => types.ForPlugin(pluginId);

    /// <summary>The plugin a widget comes from.</summary>
    public IPlugin? For(WidgetDefinition widget) => types.Find(widget.Type) is { } t ? Find(t.PluginId) : null;
}

/// <summary>
/// The shared settings of plugins (their <see cref="IPlugin.Fields"/>), stored in config.json under
/// <c>features["plugins.{id}"]</c>.
/// </summary>
public sealed class PluginSettings(ConfigStore config)
{
    public static string KeyFor(string pluginId) => $"plugins.{pluginId}";

    public Dictionary<string, string> Load(string pluginId) =>
        config.Snapshot().Feature<Dictionary<string, string>>(KeyFor(pluginId));

    public void Save(string pluginId, Dictionary<string, string> values)
    {
        var cfg = config.Snapshot();
        cfg.SetFeature(KeyFor(pluginId), values);
        config.Save(cfg);
    }

    /// <summary>
    /// A setting for <paramref name="plugin"/>: the widget's own value if it has one (older configs kept
    /// shared settings on each widget), else the plugin's, else the field default. "env:NAME" is resolved.
    /// </summary>
    public string Get(IPlugin plugin, string key, WidgetDefinition? widget = null)
    {
        if (widget is not null && widget.Settings.TryGetValue(key, out var own) && !string.IsNullOrEmpty(own))
            return WidgetSettingsExtensions.ResolveSecret(own);
        Load(plugin.Id).TryGetValue(key, out var value);
        if (string.IsNullOrEmpty(value)) value = plugin.Fields.FirstOrDefault(f => f.Key == key)?.Default ?? "";
        return WidgetSettingsExtensions.ResolveSecret(value);
    }
}
