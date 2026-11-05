using System.IO;
using System.Text.Json;
using System.Threading;

namespace GridShell.Core.Settings;

/// <summary>Persists app-wide settings to %AppData%\GridShell\settings.json.</summary>
public static class AppSettingsStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Two separate GridShell processes (the taskbar and the grid/settings UI) both
    /// read and write this same file, so a transient sharing violation - one process's Load()
    /// landing in the split second the other's Save() has the file open - is expected, not
    /// exceptional. A few short retries ride that out; if it's still locked after that (something
    /// genuinely wrong, not just timing), the write is dropped rather than crashing the whole
    /// app over one missed settings save.</summary>
    public static void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                File.WriteAllText(StorePath, json);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(20);
            }
            catch (IOException)
            {
                return;
            }
        }
    }
}
