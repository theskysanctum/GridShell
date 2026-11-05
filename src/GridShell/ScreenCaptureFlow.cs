using GridShell.Core.Interop;
using Vanara.PInvoke;

namespace GridShell;

/// <summary>The three ways to start a capture (drag-select, whole screen, a specific window),
/// each ending the same way: save + copy to clipboard + show GridShell's own "Screenshot has
/// been taken" toast. Shared by the Print Screen hotkey (always starts a Snippet) and the
/// controller screenshot menu (offers all three).</summary>
public static class ScreenCaptureFlow
{
    public static void CaptureSnippet()
    {
        var overlay = new ScreenCaptureOverlayWindow();
        overlay.Closed += (_, _) =>
        {
            if (overlay.SelectedRegion is { } region)
            {
                CaptureRegionAndShowPreview(region);
            }
        };
        overlay.Show();
    }

    public static void CaptureFullScreen() =>
        CaptureRegionAndShowPreview(ScreenCapture.FullVirtualScreenBounds());

    /// <summary>Captures the given window - takes an explicit handle rather than querying the
    /// current foreground window itself, since by the time this runs (after a menu closes) the
    /// menu's own now-closing window can still transiently be reported as foreground.</summary>
    public static void CaptureWindow(HWND hwnd)
    {
        if (ScreenCapture.WindowBounds(hwnd) is { } region)
        {
            CaptureRegionAndShowPreview(region);
        }
    }

    private static void CaptureRegionAndShowPreview(System.Drawing.Rectangle region)
    {
        var bitmap = ScreenCapture.CaptureRegion(region);
        try
        {
            var path = ScreenCapture.SaveToScreenshotsFolder(bitmap);
            ScreenCapture.CopyToClipboardAndGetSource(bitmap);
            new ScreenshotToastWindow(path).Show();
        }
        finally
        {
            bitmap.Dispose();
        }
    }
}
