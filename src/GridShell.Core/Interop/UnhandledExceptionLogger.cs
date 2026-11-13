using System.IO;
using System.Windows;

namespace GridShell.Core.Interop;

/// <summary>Last-resort handler for an exception that reached a window's DispatcherUnhandledException
/// - logs it (same %TEMP% convention as TileLauncher's own error log) and shows a brief,
/// dismissable notice so the error isn't silently swallowed, without crashing the process the
/// way an unhandled dispatcher exception does by default.</summary>
public static class UnhandledExceptionLogger
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "GridShell-errors.log");

    public static void LogAndNotify(Exception ex)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch (IOException)
        {
        }

        MessageBox.Show(
            $"Something went wrong, but GridShell kept running:\n\n{ex.Message}\n\nDetails were logged to {LogPath}.",
            "GridShell", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
