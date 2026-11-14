using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(json))
            {
                return settings;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty(nameof(AppSettings.DownloadCookies), out var cookieElement))
                {
                    var rawCookie = cookieElement.GetString();
                    if (!string.IsNullOrWhiteSpace(rawCookie))
                    {
                        settings.DownloadCookies = TryDecryptDownloadCookies(rawCookie, out var decrypted)
                            ? decrypted
                            : rawCookie;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to the default "fresh settings" path rather than crashing on a
                // malformed or partially-written settings file.
            }

            return settings;
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
        // Save the raw DownloadCookies value encrypted at rest while keeping the rest of the
        // settings file compact and backward-compatible with the existing JSON shape.
        var payload = new Dictionary<string, object?>();
        foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(settings);
            if (property.Name == nameof(AppSettings.DownloadCookies))
            {
                payload[property.Name] = EncryptDownloadCookies(value as string);
            }
            else
            {
                payload[property.Name] = value;
            }
        }

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
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

    private static string? EncryptDownloadCookies(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch (PlatformNotSupportedException)
        {
            return value;
        }
        catch (CryptographicException)
        {
            return value;
        }
    }

    private static bool TryDecryptDownloadCookies(string? value, out string? decrypted)
    {
        decrypted = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(value);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            decrypted = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
