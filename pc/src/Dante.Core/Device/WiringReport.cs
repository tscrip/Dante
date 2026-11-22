using System.Text.RegularExpressions;
using Dante.Core.Config;

namespace Dante.Core.Device;

/// <summary>
/// DANTE's answer to "Check wiring": what it found at boot and a live scan of its I2C bus,
/// e.g. "I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED)".
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

    /// <summary>
    /// The report comes from the yellow touch board (firmware 3.6.1+), which reports its touch chip
    /// ("touch panel: GT911 found at 0x5D") instead of an I2C scan.
    /// </summary>
    public bool TouchBoard => Scan.StartsWith("touch panel:", StringComparison.Ordinal);

    /// <summary>Plain-language reading of the report for the screen the PC app is set up for.</summary>
    public IReadOnlyList<string> Explain(DeviceSettings settings)
    {
        var hints = new List<string>();
        var bus = Bus();

        if (TouchBoard)
        {
            // The yellow ESP32-3248S035C: screen and touch panel are built in, so only the touch chip is worth checking.
            hints.Add(Scan.Contains("NOT FOUND", StringComparison.Ordinal)
                ? "The touch chip (GT911) didn't answer, so taps can't work. DANTE tries again a few times after starting; " +
                  "unplug DANTE for a few seconds and plug it back in. If it still isn't found, check that the board's model ends " +
                  "in \"C\" (capacitive touch): the \"R\" version has a different touch chip that DANTE doesn't support yet."
                : "The touch chip (GT911) answers, so taps should work. If they land on the wrong side, see " +
                  "\"Yellow board, taps land in the wrong place\" in firmware/README.md.");
        }
        else if (settings.Display == DisplayType.Tft35 || (settings.Display == DisplayType.Auto && !bus.Any(IsOled)))
        {
            // The TFT is on SPI with no way to read back from it: the scan can't see it.
            hints.Add("The 3.5\" TFT is on SPI, which DANTE can't detect, so the scan can't check it. If it stays white or black, check: " +
                      "SCL/SCK → GPIO 12, SDA/MOSI → GPIO 11, CS → GPIO 10, DC → GPIO 13, RST → GPIO 14, BL → GPIO 21, VCC → 3.3 V, GND → GND.");
        }
        else if (bus.Count == 0)
        {
            hints.Add("Nothing answers on the I2C bus (SDA 8 / SCL 9): check the OLED's SDA, SCL, power and GND.");
        }
        else if (!bus.Any(IsOled))
        {
            hints.Add($"The bus has {string.Join(", ", bus.Select(Hex))}, but no OLED (0x3C or 0x3D). Is the screen type set right?");
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
