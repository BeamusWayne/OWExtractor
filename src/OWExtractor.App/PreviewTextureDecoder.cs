using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OWExtractor.App;

internal sealed record DecodedPreviewTexture(BitmapSource Color, int Width, int Height, byte[] Alpha);

internal static class PreviewTextureDecoder
{
    public static DecodedPreviewTexture Decode(Stream stream, CancellationToken token)
    {
        // Decode channels BEFORE scaling. BitmapImage.DecodePixelWidth resamples premultiplied
        // alpha, destroying RGB wherever OW's packed data channel happens to be zero.
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var original = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var sw = original.PixelWidth; var sh = original.PixelHeight;
        var scale = Math.Min(1, 512d / Math.Max(sw, sh));
        var width = Math.Max(1, (int)Math.Round(sw * scale)); var height = Math.Max(1, (int)Math.Round(sh * scale));
        var pixels = new byte[width * height * 4]; var alpha = new byte[width * height];
        var row0 = new byte[sw * 4]; var row1 = new byte[sw * 4];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var sourceY = Math.Clamp((y + .5) * sh / height - .5, 0, sh - 1);
            var y0 = (int)sourceY; var y1 = Math.Min(sh - 1, y0 + 1); var fy = sourceY - y0;
            original.CopyPixels(new Int32Rect(0, y0, sw, 1), row0, sw * 4, 0);
            original.CopyPixels(new Int32Rect(0, y1, sw, 1), row1, sw * 4, 0);
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Clamp((x + .5) * sw / width - .5, 0, sw - 1);
                var x0 = (int)sourceX; var x1 = Math.Min(sw - 1, x0 + 1); var fx = sourceX - x0;
                for (var channel = 0; channel < 4; channel++)
                {
                    var top = row0[x0 * 4 + channel] * (1 - fx) + row0[x1 * 4 + channel] * fx;
                    var bottom = row1[x0 * 4 + channel] * (1 - fx) + row1[x1 * 4 + channel] * fx;
                    pixels[(y * width + x) * 4 + channel] = (byte)Math.Clamp((int)Math.Round(top * (1 - fy) + bottom * fy), 0, 255);
                }
                alpha[y * width + x] = pixels[(y * width + x) * 4 + 3];
            }
        }
        var color = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4); color.Freeze();
        return new(color, width, height, alpha);
    }
}
