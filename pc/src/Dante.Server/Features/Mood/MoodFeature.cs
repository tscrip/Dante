namespace Dante.Server.Features.Mood;

/// <summary>Mood thresholds and personality.</summary>
public sealed class MoodFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Mood & personality", "mood", 20)];
}
