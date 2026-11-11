using Dante.Core.Config;
using Dante.Core.Sources;

namespace Dante.Core.Tests;

/// <summary>The project was DeskBuddy, then HANK, now DANTE; existing setups must keep working.</summary>
public class RenameMigrationTests
{
    [Fact]
    public void Old_config_is_copied_once_and_never_overwrites()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
        var legacy = Path.Combine(root, "DeskBuddy", "config.json");
        var current = Path.Combine(root, "Dante", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "{\"personality\":\"Dramatic\"}");
        try
        {
            Assert.True(ConfigStore.MigrateLegacyConfig(legacy, current));
            Assert.Equal(Personality.Dramatic, new ConfigStore(current).Snapshot().Personality);

            File.WriteAllText(legacy, "{\"personality\":\"Sarcastic\"}");
            Assert.False(ConfigStore.MigrateLegacyConfig(legacy, current));   // new config wins from now on
            Assert.Equal(Personality.Dramatic, new ConfigStore(current).Snapshot().Personality);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("DeskBuddy")]
    [InlineData("HANK")]
    [InlineData("hank")]
    public void Old_bluetooth_names_become_dante(string old)
    {
        var cfg = new DanteConfig { Device = { BluetoothName = old } };
        ConfigStore.Validate(cfg);
        Assert.Equal("DANTE", cfg.Device.BluetoothName);
    }

    [Fact]
    public void Other_bluetooth_names_are_left_alone()
    {
        var cfg = new DanteConfig { Device = { BluetoothName = "DANTE-kitchen" } };
        ConfigStore.Validate(cfg);
        Assert.Equal("DANTE-kitchen", cfg.Device.BluetoothName);
    }

    [Fact]
    public void Old_and_new_environment_variable_names_all_resolve()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        Environment.SetEnvironmentVariable($"DESKBUDDY_PAT_{suffix}", "deskbuddy-token");
        Assert.Equal("deskbuddy-token", SourceSettingsExtensions.ResolveSecret($"env:DANTE_PAT_{suffix}"));
        Assert.Equal("deskbuddy-token", SourceSettingsExtensions.ResolveSecret($"env:HANK_PAT_{suffix}"));

        Environment.SetEnvironmentVariable($"DANTE_PAT_{suffix}", "dante-token");
        Assert.Equal("dante-token", SourceSettingsExtensions.ResolveSecret($"env:DANTE_PAT_{suffix}"));
        Assert.Equal("deskbuddy-token", SourceSettingsExtensions.ResolveSecret($"env:DESKBUDDY_PAT_{suffix}"));   // exact name first
        Assert.Equal("", SourceSettingsExtensions.ResolveSecret($"env:OTHER_PAT_{suffix}"));
    }
}
