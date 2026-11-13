using Dante.Core.Config;
using Dante.Core.Device;

namespace Dante.Core.Tests;

public class WiringReportTests
{
    private const string BootTwo = "DANTE 2.5.1: bus 1 (SDA=8 SCL=9): OLED none, LCD 0x27 | bus 2 (SDA=17 SCL=18): OLED none, LCD 0x27 | screen: lcd1602 (auto) x2 (auto)";
    private const string BootOne = "DANTE 2.5.1: bus 1 (SDA=8 SCL=9): OLED none, LCD 0x27 | bus 2 (SDA=17 SCL=18): OLED none, LCD none | screen: lcd1602 (auto) x1 (auto)";

    private static WiringReport Report(string scan, string boot = BootTwo) => new(DateTimeOffset.UtcNow, boot, scan);
    private static readonly DeviceSettings TwoLcds = new() { Display = DisplayType.Lcd1602, ScreenCount = 0 };

    [Fact]
    public void Parses_addresses_per_bus()
    {
        var r = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD) 0x3C (OLED) | bus 2 (SDA=17 SCL=18): nothing");
        Assert.Equal([0x27, 0x3C], r.Bus(1));
        Assert.Empty(r.Bus(2));
    }

    [Fact]
    public void Empty_second_bus_points_at_the_wiring()
    {
        var hints = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD) | bus 2 (SDA=17 SCL=18): nothing", BootOne).Explain(TwoLcds);
        var hint = Assert.Single(hints);
        Assert.Contains("Nothing answers on bus 2", hint);
        Assert.Contains("5 V", hint);
    }

    [Fact]
    public void Lcd_found_after_boot_needs_a_reset()
    {
        var hints = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD) | bus 2 (SDA=17 SCL=18): 0x3F (LCD)", BootOne).Explain(TwoLcds);
        Assert.Contains("Press RST", Assert.Single(hints));
    }

    [Fact]
    public void Both_found_suggests_contrast()
    {
        var hints = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD) | bus 2 (SDA=17 SCL=18): 0x27 (LCD)").Explain(TwoLcds);
        Assert.Contains("contrast", Assert.Single(hints));
    }

    [Fact]
    public void Wrong_kind_on_second_bus()
    {
        var hints = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD) | bus 2 (SDA=17 SCL=18): 0x3C (OLED)").Explain(TwoLcds);
        Assert.Contains("same kind", Assert.Single(hints));
    }

    [Fact]
    public void Brownout_is_reported_as_a_power_problem()
    {
        var boot = BootTwo + " | last reset: BROWNOUT (power dipped)";
        var hints = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED) | bus 2 (SDA=17 SCL=18): 0x3C (OLED)", boot)
            .Explain(new DeviceSettings { Display = DisplayType.Oled128x32, ScreenCount = 2 });
        Assert.Contains("brownout", hints[0]);
        Assert.Equal(2, hints.Count);
    }

    [Fact]
    public void Second_oled_at_0x3D_counts()
    {
        var hints = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED) | bus 2 (SDA=17 SCL=18): 0x3D (OLED)")
            .Explain(new DeviceSettings { Display = DisplayType.Oled128x32, ScreenCount = 0 });
        Assert.StartsWith("Both OLEDs answer", Assert.Single(hints));
    }

    [Fact]
    public void One_screen_setup_ignores_bus_two()
    {
        var hints = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED) | bus 2 (SDA=17 SCL=18): nothing")
            .Explain(new DeviceSettings { Display = DisplayType.Oled128x32, ScreenCount = 1 });
        Assert.Equal("Wiring looks right.", Assert.Single(hints));
    }
}
