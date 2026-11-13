using System.Text.RegularExpressions;
using Dante.Core.Config;

namespace Dante.Core.Device;

/// <summary>
/// DANTE's answer to "Check wiring": what it found at boot and a live scan of both I2C buses,
/// e.g. "I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD) | bus 2 (SDA=17 SCL=18): nothing".
/// </summary>
public sealed partial record WiringReport(DateTimeOffset At, string Boot, string Scan)
{
    /// <summary>Addresses answering on bus 1 / bus 2 right now.</summary>
    public IReadOnlyList<int> Bus(int bus)
    {
        var part = Scan.Split('|').FirstOrDefault(p => p.Contains($"bus {bus} ", StringComparison.Ordinal)) ?? "";
        var colon = part.IndexOf("):", StringComparison.Ordinal);
        if (colon < 0) return [];
        return [.. Address().Matches(part[(colon + 2)..]).Select(m => Convert.ToInt32(m.Groups[1].Value, 16))];
    }

    private static bool IsOled(int a) => a is 0x3C or 0x3D;
    // PCF8574A backpacks use 0x38-0x3F, which overlaps the OLED addresses; like the firmware's scan
    // labels, 0x3C/0x3D count as OLED.
    private static bool IsLcd(int a) => !IsOled(a) && (a is (>= 0x20 and <= 0x27) or (>= 0x38 and <= 0x3F));

    /// <summary>Plain-language reading of the report for the screens the PC app is set up for.</summary>
    public IReadOnlyList<string> Explain(DeviceSettings settings)
    {
        var hints = new List<string>();
        var bus1 = Bus(1);
        var bus2 = Bus(2);
        var lcd = settings.IsLcd || (settings.Display == DisplayType.Auto && bus1.Any(IsLcd) && !bus1.Any(IsOled));
        var kind = lcd ? "LCD" : "OLED";
        bool Fits(int a) => lcd ? IsLcd(a) : IsOled(a);

        if (bus1.Count == 0)
            hints.Add("Nothing answers on bus 1 (SDA 8 / SCL 9): check the first screen's SDA, SCL, power and GND.");
        else if (!bus1.Any(Fits))
            hints.Add($"Bus 1 has {string.Join(", ", bus1.Select(Hex))}, but no {kind}. Is the screen type set right?");

        var wantsTwo = settings.ScreenCount != 1;
        if (wantsTwo)
        {
            if (bus2.Count == 0)
                hints.Add($"Nothing answers on bus 2 (SDA 17 / SCL 18), so the second {kind} isn't connected as far as DANTE can tell. " +
                          "Check that its SDA goes to GPIO 17 and SCL to GPIO 18 (not swapped, and the GPIO numbers, not the board's pin positions), " +
                          (lcd ? "that it has 5 V and GND (the backlight lights up when it does), and the level shifter if you use one." : "and that it has 3.3 V and GND."));
            else if (!bus2.Any(Fits))
                hints.Add($"Bus 2 has {string.Join(", ", bus2.Select(Hex))}, but no {kind}. Both screens must be the same kind ({kind}).");
            else if (Boot.Contains(" x1", StringComparison.Ordinal))
                hints.Add($"The second {kind} answers now, but wasn't there when DANTE started. Press RST (or power-cycle) so it gets picked up.");
            else if (lcd)
                hints.Add("Both LCDs answer and DANTE is using two. If the second one still looks blank, turn the contrast screw on its backpack slowly until the characters appear.");
            else
                hints.Add("Both OLEDs answer and DANTE is using two. If one still stays dark, swap the two screens: " +
                          "if the dark one moves with the screen, that module is faulty; if it stays on the same side, it's that side's wiring.");
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
