using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.ClaudeCode;

/// <summary>Claude Code: your subscription usage, and nudges when a session is waiting on you.</summary>
public sealed class ClaudeCodePlugin : IPlugin
{
    public const string PluginId = "claude-code";
    public string Id => PluginId;
    public string Name => "Claude Code";
    public string Description =>
        "Shows how much of your Claude subscription window you've used, and nudges you the moment a Claude Code " +
        "session is waiting on you (through Claude Code hooks).";
    public int Order => 10;
    public Type ConfigComponent => typeof(ClaudeCodePanel);

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("credentialsPath", "Credentials file", SettingKind.Text, "",
            "Where your Claude Code login is. Blank = ~/.claude/.credentials.json (macOS: keychain item 'Claude Code-credentials')."),
        new("token", "Access token override", SettingKind.Secret, "", "Optional. Use env:VAR_NAME to read it from an environment variable."),
    ];
}
