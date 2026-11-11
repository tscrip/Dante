using System.Globalization;
using System.Text;

namespace Dante.Core;

public static class Format
{
    /// <summary>"45s", "12m", "3h10m", "2d4h".</summary>
    public static string Age(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalMinutes < 1) return $"{(int)t.TotalSeconds}s";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}m";
        if (t.TotalDays < 1) return t.Minutes == 0 ? $"{(int)t.TotalHours}h" : $"{(int)t.TotalHours}h{t.Minutes}m";
        return t.Hours == 0 ? $"{(int)t.TotalDays}d" : $"{(int)t.TotalDays}d{t.Hours}h";
    }

    public static string Number(double? v) =>
        v is null ? "-" :
        Math.Abs(v.Value - Math.Round(v.Value)) < 0.05 ? Math.Round(v.Value).ToString("0", CultureInfo.InvariantCulture)
        : v.Value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The OLED fonts are Latin-1 at best: fold to printable ASCII.</summary>
    public static string ToDeviceText(string? s, int maxLength)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, maxLength));
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (sb.Length >= maxLength) break;
            var mapped = ch switch
            {
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                '–' or '—' => '-',
                '…' => '.',
                '\n' or '\r' or '\t' => ' ',
                _ => ch,
            };
            if (mapped is >= ' ' and <= '~') sb.Append(mapped);
        }
        return sb.ToString().Trim();
    }
}
