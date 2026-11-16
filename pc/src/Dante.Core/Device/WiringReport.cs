using System.Text.RegularExpressions;
using Dante.Core.Config;

namespace Dante.Core.Device;

/// <summary>
/// DANTE's answer to "Check wiring": what it found at boot and a live scan of its I2C bus,
/// e.g. "I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD)".
/// </summary>
public sealed partial record WiringReport(DateTimeOffset At, string Boot, string Scan)
{
    /// <summary>Addresses answering on the I2C bus right now.</summary>
    public IReadOnlyList<int> Bus()
    {
        // Older firmware scanned two buses ("bus 1 (...): ... | bus 2 (...): ..."): only bus 1 is used now.
        var part = Scan.Split('|').FirstOrDefault(p => p.Contains("bus 1 ", StringComparison.Ordinal)) ?? Scan;
        var colon = part.IndexOf("):", StringComparison.Ordinal);
        if (colon < 0) return [];
        return [.. Address().Matches(part[(colon + 2)..]).Select(m => Convert.ToInt32(m.Groups[1].Value, 16))];
    }

    private static bool IsOled(int a) => a is 0x3C or 0x3D;
    // PCF8574A backpacks use 0x38-0x3F, which overlaps the OLED addresses; like the firmware's scan
    // labels, 0x3C/0x3D count as OLED.
    private static bool IsLcd(int a) => !IsOled(a) && (a is (>= 0x20 and <= 0x27) or (>= 0x38 and <= 0x3F));

    /// <summary>Plain-language reading of the report for the screen the PC app is set up for.</summary>
    public IReadOnlyList<string> Explain(DeviceSettings settings)
    {
        var hints = new List<string>();
        var bus = Bus();

        if (settings.Display == DisplayType.Tft35 || (settings.Display == DisplayType.Auto && bus.Count == 0))
        {
            // The TFT is on SPI with no way to read back from it: the scan can't see it.
            hints.Add("The 3.5\" TFT is on SPI, which DANTE can't detect, so the scan can't check it. If it stays white or black, check: " +
                      "SCK → GPIO 12, SDI/MOSI → GPIO 11, CS → GPIO 10, DC/RS → GPIO 13, RST → GPIO 14, LED/BL → GPIO 21, VCC → 3.3 V, GND → GND.");
            if (bus.Count > 0)
                hints.Add($"There is also something on the I2C bus ({string.Join(", ", bus.Select(Hex))}). That's fine if it isn't a screen.");
        }
        else
        {
            var lcd = settings.IsLcd || (settings.Display == DisplayType.Auto && bus.Any(IsLcd) && !bus.Any(IsOled));
            var kind = lcd ? "LCD" : "OLED";
            bool Fits(int a) => lcd ? IsLcd(a) : IsOled(a);

            if (bus.Count == 0)
                hints.Add("Nothing answers on the I2C bus (SDA 8 / SCL 9): check the screen's SDA, SCL, power and GND.");
            else if (!bus.Any(Fits))
                hints.Add($"The bus has {string.Join(", ", bus.Select(Hex))}, but no {kind}. Is the screen type set right?");
        }

        if (Boot.Contains("BROWNOUT", StringComparison.Ordinal))
            hints.Insert(0, "DANTE last restarted because its supply voltage dipped (brownout). That is a power problem: " +
                            "try a different USB cable or port (or a powered hub), and check for a short on the 3V3/5V lines.");
        if (hints.Count == 0) hints.Add("Wiring looks right.");
        return hints;
    }

    private static string Hex(int a) => $"0x{a:X2}";

    [GeneratedRegex(@"0x([0-9A-Fa-f]{2})")]
    private static partial Regex Address();
}
