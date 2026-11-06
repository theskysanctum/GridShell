using System.IO;
using System.Linq;

namespace GridShell.Core;

/// <summary>Shared video/image-wrapper-page logic for WallpaperWindow and WidgetWindow - both
/// need to navigate a WebView2 to a local or remote image/video via a tiny generated HTML page.
/// A generated wrapper page must be navigated to as a real file:// page, not via
/// NavigateToString - Chromium's "URL safety check" rejects a &lt;video&gt;/&lt;img&gt; src pointing at
/// file:// from a page that isn't itself file://, which NavigateToString's page isn't.</summary>
public static class MediaWrapperHtml
{
    public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
    public static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".mkv", ".avi" };

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path.Split('?', '#')[0]), StringComparer.OrdinalIgnoreCase);

    public static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path.Split('?', '#')[0]), StringComparer.OrdinalIgnoreCase);

    public static string BuildVideoHtml(string navigableUrl, bool muted) =>
        $"<video src=\"{navigableUrl}\" autoplay loop{(muted ? " muted" : "")} style=\"width:100vw;height:100vh;object-fit:cover\"></video>";

    public static string BuildImageHtml(string navigableUrl) =>
        $"<img src=\"{navigableUrl}\" style=\"width:100vw;height:100vh;object-fit:cover\" />";

    public static bool LooksLikeRemoteUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

    public static string ToNavigableUrl(string value) =>
        LooksLikeRemoteUrl(value) ? value : new Uri(Path.GetFullPath(value)).AbsoluteUri;

    /// <summary>Writes bodyHtml wrapped in a minimal page shell to wrapperPath and returns the
    /// file:// Uri to navigate to.</summary>
    public static string WriteAndGetUri(string wrapperPath, string bodyHtml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(wrapperPath)!);
        File.WriteAllText(wrapperPath,
            $"<html><body style=\"margin:0;overflow:hidden;background:#101010\">{bodyHtml}</body></html>");
        return new Uri(wrapperPath).AbsoluteUri;
    }
}
