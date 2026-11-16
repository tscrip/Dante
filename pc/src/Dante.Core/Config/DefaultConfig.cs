namespace Dante.Core.Config;

/// <summary>The first-run config: the three example apps from the design brief.</summary>
public static class DefaultConfig
{
    public static DanteConfig Create() => new()
    {
        Apps =
        [
            new AppDefinition
            {
                Id = "cc_usage",
                Name = "Claude 5h",
                Visual = VisualKind.Gauge,
                Min = 0, Max = 100, Unit = "%",
                SourceType = "claude-code-usage",
                SourceSettings = new() { ["window"] = "five_hour" },
                PollSeconds = 120,
                Levels = new LevelRule { WarnAt = 70, CritAt = 90 },
                QuipMinDelta = 10,
                OnSelectUrl = "https://claude.ai/settings/usage",
            },
            new AppDefinition
            {
                Id = "cc_wait",
                Name = "Claude Code",
                Visual = VisualKind.Status,
                Min = 0, Max = 5,
                SourceType = "claude-code-waiting",
                PollSeconds = 5,
                CountsTowardMood = true,
                Levels = new LevelRule { WarnAt = 1, CritAt = 3 },
                Takeover = new TakeoverRule
                {
                    Mode = TakeoverMode.WhenPending,
                    DelaySeconds = 20,
                    RenudgeMinutes = 5,
                    SnoozeMinutes = 5,
                    Level = 1,
                    Message = "{item} is waiting on you!",
                },
            },
            new AppDefinition
            {
                Id = "prs",
                Name = "PRs for me",
                Enabled = false, // fill in organisation + PAT first
                Visual = VisualKind.Counter,
                Min = 0, Max = 20,
                SourceType = "azure-devops-prs",
                SourceSettings = new()
                {
                    ["organization"] = "https://dev.azure.com/your-org",
                    ["project"] = "",
                    ["pat"] = "env:DANTE_ADO_PAT",
                    ["mode"] = "review",
                },
                PollSeconds = 300,
                CountsTowardMood = true,
                Levels = new LevelRule { WarnAt = 3, CritAt = 6 },
                OnSelectUrl = "{url}",
            },
        ],
    };
}
