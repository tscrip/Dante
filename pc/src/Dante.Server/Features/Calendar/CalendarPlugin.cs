using Dante.Core.Plugins;

namespace Dante.Server.Features.Calendar;

/// <summary>Meeting reminders from your calendar.</summary>
public sealed class CalendarPlugin : IPlugin
{
    public const string PluginId = "calendar";
    public string Id => PluginId;
    public string Name => "Calendar";
    public string Description =>
        "DANTE reminds you before meetings start. On the reminder, SELECT (\"I'm on it\") opens the meeting's Teams / Zoom " +
        "link on this PC and stops the reminders for that meeting; LEFT/RIGHT (\"later\") waits for the next reminder.";
    public int Order => 30;
    public Type ConfigComponent => typeof(CalendarPanel);
}
