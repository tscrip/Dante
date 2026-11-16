using Dante.Core.Plugins;

namespace Dante.Server.Features.Pomodoro;

/// <summary>A Pomodoro focus timer.</summary>
public sealed class PomodoroPlugin : IPlugin
{
    public const string PluginId = "pomodoro";
    public string Id => PluginId;
    public string Name => "Pomodoro";
    public string Description =>
        "Focus in short sessions with breaks in between. Go to the Pomodoro widget on DANTE with LEFT/RIGHT, then press " +
        "MAGIC to start or pause (hold MAGIC to reset). When a session ends DANTE tells you, and SELECT starts the next one.";
    public int Order => 40;
    public Type ConfigComponent => typeof(PomodoroPanel);
}
