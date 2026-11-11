using Hank.Core.Config;
using Hank.Core.Device;
using Hank.Core.Engine;
using Microsoft.AspNetCore.Components;

namespace Hank.Server.Components.Shared;

/// <summary>Base for pages that show live state: re-renders on engine/link events and once a second.</summary>
public abstract class LiveComponent : ComponentBase, IDisposable
{
    [Inject] protected HankEngine Engine { get; set; } = default!;
    [Inject] protected DeviceLink Link { get; set; } = default!;

    private Timer? _timer;

    protected override void OnInitialized()
    {
        Engine.StateChanged += Refresh;
        Link.StatusChanged += Refresh;
        _timer = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void Refresh() => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        Engine.StateChanged -= Refresh;
        Link.StatusChanged -= Refresh;
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected static string LevelClass(int level) => level switch { 2 => "crit", 1 => "warn", _ => "ok" };

    protected static string MoodFace(Mood mood) => mood switch
    {
        Mood.Happy => "(^‿^)",
        Mood.Curious => "(o_O)",
        Mood.Antsy => "(°_°;)",
        Mood.Grumpy => "(ಠ_ಠ)",
        Mood.Meltdown => "(╯°□°)╯",
        _ => "(-_-)",
    };
}
