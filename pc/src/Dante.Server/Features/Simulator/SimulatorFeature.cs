namespace Dante.Server.Features.Simulator;

/// <summary>
/// The DANTE simulator: the real firmware built for this PC (<c>pio run -e simulator</c>), started and
/// shown live by the app, with clickable buttons. The connection itself lives in the kernel
/// (<see cref="Dante.Core.Device.SimulatorTransport"/>); this slice is the page.
/// </summary>
public sealed class SimulatorFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Simulator", "simulator", 85)];   // just above Device (90)
}
