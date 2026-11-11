using Hank.Core.Config;
using Hank.Core.Sources;

namespace Hank.Core.Tests;

/// <summary>The project used to be called DeskBuddy; existing setups must keep working.</summary>
public class RenameMigrationTests
{
    [Fact]
    public void Old_config_is_copied_once_and_never_overwrites()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hank-{Guid.NewGuid():N}");
        var legacy = Path.Combine(root, "DeskBuddy", "config.json");
        var current = Path.Combine(root, "Hank", "config.json");
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

    [Fact]
    public void Old_bluetooth_name_becomes_hank()
    {
        var cfg = new HankConfig { Device = { BluetoothName = "DeskBuddy" } };
        ConfigStore.Validate(cfg);
        Assert.Equal("HANK", cfg.Device.BluetoothName);

        cfg.Device.BluetoothName = "HANK-kitchen";   // anything else is left alone
        ConfigStore.Validate(cfg);
        Assert.Equal("HANK-kitchen", cfg.Device.BluetoothName);
    }

    [Fact]
    public void Old_and_new_environment_variable_names_both_resolve()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        Environment.SetEnvironmentVariable($"DESKBUDDY_PAT_{suffix}", "old-token");
        Assert.Equal("old-token", SourceSettingsExtensions.ResolveSecret($"env:HANK_PAT_{suffix}"));
        Assert.Equal("old-token", SourceSettingsExtensions.ResolveSecret($"env:DESKBUDDY_PAT_{suffix}"));

        Environment.SetEnvironmentVariable($"HANK_PAT_{suffix}", "new-token");
        Assert.Equal("new-token", SourceSettingsExtensions.ResolveSecret($"env:HANK_PAT_{suffix}"));
        Assert.Equal("old-token", SourceSettingsExtensions.ResolveSecret($"env:DESKBUDDY_PAT_{suffix}"));   // exact name first
    }
}
