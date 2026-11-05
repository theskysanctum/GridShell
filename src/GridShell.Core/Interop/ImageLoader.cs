using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridShell.Core.Interop;

/// <summary>Loads an ImageSource from an arbitrary local image file (for favicons and user-chosen custom icons).</summary>
public static class ImageLoader
{
    public static ImageSource? LoadFromFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
