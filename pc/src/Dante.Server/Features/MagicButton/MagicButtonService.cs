using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;

namespace Dante.Server.Features.MagicButton;

/// <summary>
/// Runs the PC action configured for the MAGIC button. MAGIC is reserved for this action: it does the
/// same thing on every screen (mute while music plays, whatever DANTE is showing).
/// </summary>
public sealed class MagicButtonService(ConfigStore config, IPcActions actions, DanteEngine engine, DeviceLink link,
    ILogger<MagicButtonService> log) : IDeviceEventHandler
{
    /// <summary>Runs after every screen-specific handler.</summary>
    public const int HandlerOrder = 1000;

    public bool Handle(DeviceEvent evt, WidgetDefinition? screen)
    {
        if (evt.Type != "btn" || evt.Button != "magic") return false;
        _ = RunAsync(evt.Long);   // never block the device link on a slow action
        return true;
    }

    /// <summary>Runs the MAGIC button's action (also used by the "Try it" button in the UI).</summary>
    /// <returns>What happened, e.g. "Mic muted" (also shown on DANTE if configured).</returns>
    public async Task<string> RunAsync(bool longPress, CancellationToken ct = default)
    {
        var settings = config.Snapshot().Feature<MagicButtonSettings>(MagicButtonSettings.Key);
        var action = longPress ? settings.LongPress : settings.Press;
        if (action.Kind == MagicActionKind.Nothing) return "";

        string message;
        try
        {
            if (action.Kind == MagicActionKind.RefreshApps)
            {
                engine.PollAll();
                message = "Refreshing apps!";
            }
            else
            {
                message = await actions.RunAsync(action, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Magic button action {Action} failed", action.Kind);
            message = ex is ActionFailedException or PlatformNotSupportedException ? ex.Message : $"Magic failed: {ex.Message}";
            link.Send(DeviceProtocol.Say(message));   // always tell the human when it didn't work
            return message;
        }
        if (action.ConfirmOnBuddy && !string.IsNullOrWhiteSpace(message)) link.Send(DeviceProtocol.Say(message));
        return message;
    }
}
