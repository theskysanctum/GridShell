using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GridShell.Core.Controllers;

/// <summary>One raw HID interface HidHide can hide, flattened out of --dev-gaming's nested
/// per-physical-device JSON (a device with multiple interfaces, like the dual-chipset NACON pad
/// diagnosed in an earlier session, lists one entry per interface under one friendlyName).</summary>
public readonly record struct HiddenCandidateDevice(
    string FriendlyName,
    string DeviceInstancePath,
    ushort VendorId,
    ushort ProductId);

/// <summary>Shells out to HidHide's bundled CLI tool (HidHideCLI.exe) - no C# client library for
/// HidHide exists, confirmed by search. Locates the exe via the same uninstall-registry
/// InstallLocation value Windows itself uses, rather than a hardcoded path, so a different
/// install location (or future HidHide version) doesn't silently break this. Each call is a
/// single non-interactive invocation (confirmed empirically: a single flag like --dev-gaming
/// prints its result and exits immediately, it does not drop into HidHideCLI's interactive
/// mode unless launched with zero arguments).</summary>
public static class HidHideBridge
{
    public sealed class NotInstalledException : Exception
    {
        public NotInstalledException() : base("HidHide is not installed.")
        {
        }
    }

    private static string? _cliPath;

    private static string CliPath
    {
        get
        {
            if (_cliPath is not null)
            {
                return _cliPath;
            }

            const string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{01E0AB21-D1CC-42B4-9DFF-84FFE4F26DAF}";
            var installLocation = (string?)Registry.LocalMachine.OpenSubKey(uninstallKey)?.GetValue("InstallLocation");
            if (installLocation is null)
            {
                throw new NotInstalledException();
            }

            var path = Path.Combine(installLocation, "x64", "HidHideCLI.exe");
            if (!File.Exists(path))
            {
                throw new NotInstalledException();
            }

            _cliPath = path;
            return _cliPath;
        }
    }

    public static bool IsInstalled()
    {
        try
        {
            return File.Exists(CliPath);
        }
        catch (NotInstalledException)
        {
            return false;
        }
    }

    /// <summary>Whitelists an exe (by full path) so it keeps seeing hidden devices - callers must
    /// register GridShell's own exe(s) before hiding any device their own feed loop reads from,
    /// or the hide would also block GridShell itself.</summary>
    public static void RegisterApplication(string exePath) => Run($"--app-reg \"{exePath}\"");

    public static void UnregisterApplication(string exePath) => Run($"--app-unreg \"{exePath}\"");

    public static void HideDevice(string deviceInstancePath) => Run($"--dev-hide \"{deviceInstancePath}\"");

    public static void UnhideDevice(string deviceInstancePath) => Run($"--dev-unhide \"{deviceInstancePath}\"");

    public static void SetCloakEnabled(bool enabled) => Run(enabled ? "--cloak-on" : "--cloak-off");

    public static string ListHiddenDevices() => Run("--dev-list");

    public static string ListGamingDevices() => Run("--dev-gaming");

    private static readonly Regex VidPidPattern = new(@"VID_([0-9A-Fa-f]{4}).*?PID_([0-9A-Fa-f]{4})", RegexOptions.Compiled);

    /// <summary>Parses --dev-gaming's JSON into one entry per raw HID interface, so a device
    /// exposing more than one interface under the same friendlyName (baseContainerDeviceCount > 1)
    /// shows up as multiple hideable entries rather than being collapsed into one.</summary>
    public static IReadOnlyList<HiddenCandidateDevice> ListGamingDevicesParsed()
    {
        var json = ListGamingDevices();
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<HiddenCandidateDevice>();
        }

        var result = new List<HiddenCandidateDevice>();
        using var doc = JsonDocument.Parse(json);
        foreach (var physicalDevice in doc.RootElement.EnumerateArray())
        {
            var friendlyName = physicalDevice.GetProperty("friendlyName").GetString() ?? "Unknown Device";
            foreach (var iface in physicalDevice.GetProperty("devices").EnumerateArray())
            {
                var instancePath = iface.GetProperty("deviceInstancePath").GetString();
                if (instancePath is null)
                {
                    continue;
                }

                var match = VidPidPattern.Match(instancePath);
                if (!match.Success)
                {
                    continue;
                }

                result.Add(new HiddenCandidateDevice(
                    friendlyName,
                    instancePath,
                    Convert.ToUInt16(match.Groups[1].Value, 16),
                    Convert.ToUInt16(match.Groups[2].Value, 16)));
            }
        }

        return result;
    }

    private static string Run(string arguments)
    {
        var startInfo = new ProcessStartInfo(CliPath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start HidHideCLI.exe.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"HidHideCLI.exe {arguments} failed (exit {process.ExitCode}): {error}");
        }

        return output.Trim();
    }
}
