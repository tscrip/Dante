using Dante.Core.Device;
using SkiaSharp;

namespace Dante.Server.Features.MediaPlayer;

/// <summary>
/// Turns album art (any size, JPEG / PNG / WebP...) into what DANTE can hold: a small square JPEG. DANTE has
/// very little memory, so the picture is cropped to a square, shrunk, and compressed until it fits.
/// Uses SkiaSharp (MIT licence), the graphics library .NET MAUI is built on.
/// </summary>
public static class AlbumArt
{
    /// <summary>Pixels per side on DANTE's colour screen.</summary>
    public const int Size = 120;
    private const int MaxInputBytes = 10 * 1024 * 1024;

    /// <returns>A baseline JPEG of <see cref="Size"/>×<see cref="Size"/> pixels, or null if the picture can't be read.</returns>
    public static byte[]? ToDeviceJpeg(byte[]? image)
    {
        if (image is not { Length: > 0 and <= MaxInputBytes }) return null;
        using var source = SKImage.FromEncodedData(image);
        if (source is null) return null;   // not a picture we understand: show no art rather than fail

        var side = Math.Min(source.Width, source.Height);
        var crop = SKRect.Create((source.Width - side) / 2f, (source.Height - side) / 2f, side, side);
        using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);   // see-through PNGs end up on black, like DANTE's screen
        surface.Canvas.DrawImage(source, crop, SKRect.Create(Size, Size), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var small = surface.Snapshot();

        // Skia writes baseline (not progressive) JPEGs: the decoder on DANTE only reads those.
        foreach (var quality in new[] { 82, 68, 50 })
        {
            using var data = small.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (data is not null && data.Size <= DeviceProtocol.MaxArtBytes) return data.ToArray();
        }
        return null;
    }
}
