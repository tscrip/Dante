namespace Dante.Server.Features.Device;

/// <summary>Connection, screen type and wiring checks.</summary>
public sealed class DeviceFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Device", "device", 90)];
}
