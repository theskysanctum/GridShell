using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GridShell.Core.Interop;

/// <summary>Reads the path of the image Windows currently considers "the" desktop wallpaper
/// (SPI_GETDESKWALLPAPER) — used as the default WallpaperWindow content so a fresh install
/// shows something reasonable instead of a blank screen, without requiring the user to pick
/// anything first.</summary>
public static class DesktopWallpaper
{
    private const uint SPI_GETDESKWALLPAPER = 0x0073;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

    public static string? GetCurrentPath()
    {
        var sb = new StringBuilder(260);
        if (!SystemParametersInfoW(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
        {
            return null;
        }

        var path = sb.ToString();
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : path;
    }
}
