using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Extracts a real Windows shell icon for a file/shortcut path.</summary>
public static class ShellIcons
{
    public static ImageSource? GetIcon(string path)
    {
        // "shell:AppsFolder\..." paths (from AppTileEnumerator) name a virtual shell item, not
        // a real file — SHGetFileInfo can't resolve those, so they need the shell item's own
        // thumbnail/icon API instead.
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return GetIconForShellPath(path);
        }

        var flags = SHGFI.SHGFI_ICON | SHGFI.SHGFI_LARGEICON;
        return GetIconCore(path, 0, flags);
    }

    private static ImageSource? GetIconForShellPath(string shellPath)
    {
        try
        {
            using var item = ShellItem.Open(shellPath);
            using var hbitmap = item.GetImage(new SIZE(32, 32), ShellItemGetImageOptions.IconOnly);
            if (hbitmap.IsNull)
            {
                return null;
            }

            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap.DangerousGetHandle(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Icon for a top-level window. Tries the window's own icon first (via WM_GETICON) —
    /// this is what makes a browser's "--app=" website-tile window show the site's own favicon
    /// instead of the browser's generic icon, since Chromium sets a distinct per-window icon for
    /// each app-mode window. Falls back to the owning process's executable icon (the same real
    /// icon Explorer's own taskbar would show) for windows that don't set their own.</summary>
    public static ImageSource? GetIconForWindow(HWND hwnd)
    {
        return GetWindowOwnIcon(hwnd) ?? GetProcessIconForWindow(hwnd);
    }

    /// <summary>The window's own icon only (no exe-icon fallback) — null if it hasn't set one
    /// yet. A freshly created "--app=" window can take a moment after it first appears before
    /// Chromium finishes fetching the page's favicon and calls WM_SETICON with it, so callers
    /// that cache a window's icon should keep retrying this specifically (not the exe-fallback
    /// one) until it stops returning null, rather than settling for whatever was available at
    /// the very first instant.</summary>
    public static ImageSource? GetWindowOwnIcon(HWND hwnd)
    {
        // ICON_BIG=1, then ICON_SMALL2=2 — a short timeout and SMTO_ABORTIFHUNG so a single
        // unresponsive window can't stall the whole taskbar refresh.
        foreach (var iconType in new IntPtr[] { 1, 2 })
        {
            var hIcon = IntPtr.Zero;
            var responded = SendMessageTimeout(hwnd, (uint)WindowMessage.WM_GETICON, iconType, IntPtr.Zero,
                SMTO.SMTO_ABORTIFHUNG, 200, ref hIcon);

            if (responded != IntPtr.Zero && hIcon != IntPtr.Zero)
            {
                // Owned by the target window, not by us — wrap and Freeze (which copies the
                // pixel data), then leave the source handle alone. Must NOT DestroyIcon it.
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze();
                return bitmapSource;
            }
        }

        return null;
    }

    /// <summary>Icon for the process owning a window (not the window's own icon) — the exe-icon
    /// fallback used when a window hasn't set its own.</summary>
    public static ImageSource? GetProcessIconForWindow(HWND hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var exePath = process.MainModule?.FileName;
            return exePath is not null ? GetIcon(exePath) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Elevated process we can't inspect, or it exited mid-lookup.
            return null;
        }
    }

    /// <summary>A generic icon for a file type, looked up by extension only (no real file needed) —
    /// used for website tiles, which have no file on disk to extract an icon from.</summary>
    public static ImageSource? GetGenericIcon(string extension)
    {
        var flags = SHGFI.SHGFI_ICON | SHGFI.SHGFI_LARGEICON | SHGFI.SHGFI_USEFILEATTRIBUTES;
        return GetIconCore("placeholder" + extension, FileAttributes.Normal, flags);
    }

    private static ImageSource? GetIconCore(string path, FileAttributes attributes, SHGFI flags)
    {
        var shfi = new SHFILEINFO();
        var result = SHGetFileInfo(path, attributes, ref shfi, Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || shfi.hIcon.IsNull)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon.DangerousGetHandle(),
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            User32.DestroyIcon(shfi.hIcon);
        }
    }
}
