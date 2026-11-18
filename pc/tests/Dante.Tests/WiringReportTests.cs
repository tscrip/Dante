using Dante.Core.Config;
using Dante.Core.Device;

namespace Dante.Tests;

public class WiringReportTests
{
    private const string Boot = "DANTE 3.2.0: I2C (SDA=8 SCL=9): OLED 0x3C | TFT (SPI, SCK=12 MOSI=11 CS=10 DC=13 RST=14 BL=21) | screen: oled (auto) | last reset: power on";

    private static WiringReport Report(string scan, string boot = Boot) => new(DateTimeOffset.UtcNow, boot, scan);
    private static readonly DeviceSettings Oled = new() { Display = DisplayType.Oled128x32 };

    [Fact]
    public void Parses_addresses_on_the_bus()
    {
        Assert.Equal([0x27, 0x3C], Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 0x3C (OLED)").Bus());
        Assert.Empty(Report("I2C scan now: bus 1 (SDA=8 SCL=9): nothing").Bus());
    }

    [Fact]
    public void Reports_from_older_two_bus_firmware_only_count_bus_one()
    {
        var r = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED) | bus 2 (SDA=17 SCL=18): 0x3D (OLED)");
        Assert.Equal([0x3C], r.Bus());
    }

    [Fact]
    public void Empty_bus_points_at_the_wiring()
    {
        var hint = Assert.Single(Report("I2C scan now: bus 1 (SDA=8 SCL=9): nothing").Explain(Oled));
        Assert.Contains("Nothing answers on the I2C bus", hint);
    }

    [Fact]
    public void Something_else_on_the_bus_but_no_OLED()
    {
        var hint = Assert.Single(Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x27").Explain(Oled));
        Assert.Contains("no OLED", hint);
    }

    [Fact]
    public void OLED_found_at_either_address()
    {
        Assert.Equal("Wiring looks right.", Assert.Single(Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED)").Explain(Oled)));
        Assert.Equal("Wiring looks right.", Assert.Single(Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x3D (OLED)").Explain(Oled)));
        Assert.Equal("Wiring looks right.", Assert.Single(Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED)").Explain(new DeviceSettings())));
    }

    [Fact]
    public void Tft_cannot_be_scanned_so_the_pins_are_listed()
    {
        var tft = new DeviceSettings { Display = DisplayType.Tft35 };
        var hint = Assert.Single(Report("I2C scan now: bus 1 (SDA=8 SCL=9): nothing").Explain(tft));
        Assert.Contains("SPI", hint);
        Assert.Contains("GPIO 12", hint);
        // Automatic with no OLED: DANTE falls back to the TFT, so explain that instead of "nothing answers".
        Assert.Contains("SPI", Assert.Single(Report("I2C scan now: bus 1 (SDA=8 SCL=9): nothing").Explain(new DeviceSettings())));
    }

    [Fact]
    public void Brownout_is_reported_as_a_power_problem()
    {
        var hints = Report("I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED)", Boot.Replace("power on", "BROWNOUT (power dipped)")).Explain(Oled);
        Assert.Contains("brownout", Assert.Single(hints));   // the screen itself answers fine
    }
}
