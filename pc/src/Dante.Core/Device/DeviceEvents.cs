using Dante.Core.Config;

namespace Dante.Core.Device;

/// <summary>
/// Lets a feature slice react to DANTE's buttons. The engine asks every handler, in registration order,
/// before doing its own thing; the first one that returns true owns the event.
/// </summary>
public interface IDeviceEventHandler
{
    /// <param name="evt">The message from DANTE.</param>
    /// <param name="screen">The app on screen when the button was pressed, or null on the face.</param>
    /// <returns>True if the event was handled and the engine should not act on it too.</returns>
    bool Handle(DeviceEvent evt, AppDefinition? screen);
}

/// <summary>A nudge raised by a feature (not by an app's takeover rule).</summary>
public sealed record FeatureAlert(string Id, string Title, string Text, int Level);

/// <summary>
/// Nudges that features raise directly (a meeting is about to start, a focus session ended). Tracks
/// what is showing so it is re-sent when DANTE reconnects, and tells the owner when the human answers.
/// </summary>
public sealed class DeviceAlerts(DeviceLink link)
{
    private readonly Dictionary<string, FeatureAlert> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>The human pressed SELECT ("I'm on it") on this alert.</summary>
    public event Action<FeatureAlert>? Acknowledged;
    /// <summary>The human pressed LEFT/RIGHT ("later") on this alert.</summary>
    public event Action<FeatureAlert>? Snoozed;
    public event Action? Changed;

    public IReadOnlyList<FeatureAlert> Active
    {
        get { lock (_gate) return [.. _active.Values]; }
    }

    public bool IsActive(string id)
    {
        lock (_gate) return _active.ContainsKey(id);
    }

    /// <summary>Takes over DANTE's screen until the human answers or the alert is cleared.</summary>
    public void Raise(string id, string title, string text, int level = 1)
    {
        var alert = new FeatureAlert(id, title, text, Math.Clamp(level, 1, 2));
        lock (_gate)
        {
            if (_active.TryGetValue(id, out var existing) && existing == alert) return;
            _active[id] = alert;
        }
        link.Send(DeviceProtocol.Alert(alert.Id, alert.Title, alert.Text, alert.Level));
        Changed?.Invoke();
    }

    public void Clear(string id)
    {
        lock (_gate)
            if (!_active.Remove(id)) return;
        link.Send(DeviceProtocol.Clear(id));
        Changed?.Invoke();
    }

    /// <summary>A speech bubble on DANTE.</summary>
    public void Say(string text) => link.Send(DeviceProtocol.Say(text));

    /// <summary>After DANTE (re)connects: show everything that is still active.</summary>
    public void ResendAll()
    {
        foreach (var a in Active) link.Send(DeviceProtocol.Alert(a.Id, a.Title, a.Text, a.Level));
    }

    /// <summary>Called by the engine for every `ack` / `snooze`. Returns true if the alert was a feature's.</summary>
    public bool TryAnswer(DeviceEvent evt)
    {
        if (evt.Type is not ("ack" or "snooze") || evt.Id is not { } id) return false;
        FeatureAlert? alert;
        lock (_gate)
            if (!_active.Remove(id, out alert)) return false;   // DANTE already took it off its screen
        if (evt.Type == "ack") Acknowledged?.Invoke(alert);
        else Snoozed?.Invoke(alert);
        Changed?.Invoke();
        return true;
    }
}
