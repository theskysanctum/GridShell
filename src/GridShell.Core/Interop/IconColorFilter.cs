using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridShell.Core.Interop;

public static class IconColorFilter
{
    public static ImageSource? ToMonochrome(ImageSource? source)
    {
        if (source is not BitmapSource bitmap)
        {
            return source;
        }

        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        for (var index = 0; index < pixels.Length; index += 4)
        {
            var gray = (byte)((pixels[index] * 11 + pixels[index + 1] * 59 + pixels[index + 2] * 30) / 100);
            pixels[index] = gray;
            pixels[index + 1] = gray;
            pixels[index + 2] = gray;
        }

        var monochrome = new WriteableBitmap(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            bitmap.DpiX,
            bitmap.DpiY,
            PixelFormats.Bgra32,
            null);
        monochrome.WritePixels(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight), pixels, stride, 0);
        monochrome.Freeze();
        return monochrome;
    }
}
