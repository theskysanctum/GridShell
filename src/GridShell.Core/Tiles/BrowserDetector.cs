using System.IO;
using Microsoft.Win32;

namespace GridShell.Core.Tiles;

public record DetectedBrowser(string Name, string ExecutablePath);

/// <summary>Finds installed Chromium browsers via the Windows "App Paths" registry key.</summary>
public static class BrowserDetector
{
    private static readonly (string Name, string ExeName)[] Known =
    {
        ("Microsoft Edge", "msedge.exe"),
        ("Google Chrome", "chrome.exe"),
        ("Brave", "brave.exe"),
    };

    public static List<DetectedBrowser> DetectInstalled()
    {
        var found = new List<DetectedBrowser>();

        foreach (var (name, exeName) in Known)
        {
            var path = LookupAppPath(exeName);
            if (path is not null)
            {
                found.Add(new DetectedBrowser(name, path));
            }
        }

        return found;
    }

    private static string? LookupAppPath(string exeName)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
                var value = key?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
                {
                    return value;
                }
            }
            catch (System.Security.SecurityException) { }
        }

        return null;
    }
}
