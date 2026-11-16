namespace Dante.Core.Config;

/// <summary>The first-run config: three example widgets (the plugins' shared settings start at their defaults).</summary>
public static class DefaultConfig
{
    public static DanteConfig Create() => new()
    {
        Widgets =
        [
            new WidgetDefinition
            {
                Id = "cc_usage",
                Name = "Claude 5h",
                Visual = VisualKind.Gauge,
                Min = 0, Max = 100, Unit = "%",
                Type = "claude-code-usage",
                Settings = new() { ["window"] = "five_hour" },
                PollSeconds = 120,
                Levels = new LevelRule { WarnAt = 70, CritAt = 90 },
                QuipMinDelta = 10,
                OnSelectUrl = "https://claude.ai/settings/usage",
            },
            new WidgetDefinition
            {
                Id = "cc_wait",
                Name = "Claude Code",
                Visual = VisualKind.Status,
                Min = 0, Max = 5,
                Type = "claude-code-waiting",
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
            new WidgetDefinition
            {
                Id = "prs",
                Name = "PRs for me",
                Active = false, // set the organisation + PAT on the Azure DevOps plugin page first
                Visual = VisualKind.Counter,
                Min = 0, Max = 20,
                Type = "azure-devops-prs",
                Settings = new()
                {
                    ["project"] = "",
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
