using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Vanara.PInvoke;
using static Vanara.PInvoke.DwmApi;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Captures a region of the screen (or the full virtual screen, all monitors), saves it
/// as a PNG to Pictures\Screenshots (matching Windows' own default screenshot folder
/// convention), and copies it to the clipboard.</summary>
public static class ScreenCapture
{
    private static readonly string ScreenshotsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static Rectangle FullVirtualScreenBounds() => new(
        GetSystemMetrics(SystemMetric.SM_XVIRTUALSCREEN),
        GetSystemMetrics(SystemMetric.SM_YVIRTUALSCREEN),
        GetSystemMetrics(SystemMetric.SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SystemMetric.SM_CYVIRTUALSCREEN));

    /// <summary>A window's bounds, in physical screen pixels - via DWM's extended frame bounds
    /// (excludes the invisible resize-shadow margin a plain GetWindowRect would include, same
    /// gotcha WindowEnumerator already works around elsewhere), falling back to GetWindowRect
    /// for a window DWM won't report on (seen for windows owned by a different integrity
    /// level). Takes an explicit handle rather than querying GetForegroundWindow() itself - a
    /// caller that's about to show its own window (like the screenshot menu) needs to capture
    /// this BEFORE doing so, since that steals foreground focus for itself.</summary>
    public static Rectangle? WindowBounds(HWND hwnd)
    {
        if (hwnd.IsNull)
        {
            return null;
        }

        RECT rect;
        try
        {
            DwmGetWindowAttribute<RECT>(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out rect);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!GetWindowRect(hwnd, out rect))
            {
                return null;
            }
        }

        return new Rectangle(rect.left, rect.top, rect.Width, rect.Height);
    }

    public static Bitmap CaptureRegion(Rectangle region)
    {
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size);
        return bitmap;
    }

    public static string? SaveToScreenshotsFolder(Bitmap bitmap)
    {
        try
        {
            Directory.CreateDirectory(ScreenshotsDir);
            var path = Path.Combine(ScreenshotsDir, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>Copies the bitmap to the clipboard and returns the equivalent WPF BitmapSource,
    /// so a caller that also wants to display it (e.g. a preview window) doesn't need a second,
    /// redundant HBitmap conversion.</summary>
    public static BitmapSource? CopyToClipboardAndGetSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            System.Windows.Clipboard.SetImage(source);
            return source;
        }
        catch (ExternalException)
        {
            return null;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    /// <summary>One-shot full-screen capture with no region picker - saves + copies to
    /// clipboard immediately. Kept for callers that don't need the interactive overlay.</summary>
    public static string? CaptureAndSave()
    {
        using var bitmap = CaptureRegion(FullVirtualScreenBounds());
        var path = SaveToScreenshotsFolder(bitmap);
        CopyToClipboardAndGetSource(bitmap);
        return path;
    }
}
