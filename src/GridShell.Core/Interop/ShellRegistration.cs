using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GridShell.Core.Interop;

/// <summary>Registers/unregisters GridShell.Watchdog as the machine's Windows shell, via
/// HKLM\...\Winlogon\Shell. A per-user HKCU override of this key was tried first (so no other
/// account or Safe Mode boot would be affected) but Winlogon on this machine simply ignored it —
/// confirmed by testing a real sign-out/sign-in: Explorer kept running as shell regardless.
/// HKLM is the only path Microsoft's own troubleshooting docs and real-world reports agree
/// Winlogon actually reads, so that's what this now writes — machine-wide, affecting every
/// account and Safe Mode. Because of that, every write goes through an elevated "reg.exe"
/// call (triggers a real UAC prompt) instead of writing directly, so GridShell.Grid itself
/// stays unprivileged and the user sees exactly what's being changed.</summary>
public static class ShellRegistration
{
    private const string WinlogonKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string ShellValueName = "Shell";

    private static readonly string PreviousShellBackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "previous-shell.txt");

    public static bool IsGridShellRegisteredAsShell(string watchdogExePath)
    {
        using var key = Registry.LocalMachine.OpenSubKey(WinlogonKeyPath);
        var current = key?.GetValue(ShellValueName) as string;
        return current is not null && string.Equals(current, watchdogExePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>false if the elevation prompt was declined or the write otherwise failed —
    /// callers must not assume the switch took effect.</returns>
    public static bool RegisterAsShell(string watchdogExePath)
    {
        using (var key = Registry.LocalMachine.OpenSubKey(WinlogonKeyPath))
        {
            var previous = key?.GetValue(ShellValueName) as string;
            if (!string.IsNullOrEmpty(previous) && !string.Equals(previous, watchdogExePath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreviousShellBackupPath)!);
                File.WriteAllText(PreviousShellBackupPath, previous);
            }
        }

        return RunElevatedRegAdd(watchdogExePath);
    }

    /// <returns>false if the elevation prompt was declined or the write otherwise failed —
    /// callers must not assume the switch took effect.</returns>
    public static bool UnregisterAsShell()
    {
        var restoreValue = File.Exists(PreviousShellBackupPath)
            ? File.ReadAllText(PreviousShellBackupPath).Trim()
            : "";

        if (string.IsNullOrWhiteSpace(restoreValue))
        {
            restoreValue = "explorer.exe";
        }

        return RunElevatedRegAdd(restoreValue);
    }

    private static bool RunElevatedRegAdd(string shellValue)
    {
        var arguments = $"add \"HKLM\\{WinlogonKeyPath}\" /v {ShellValueName} /t REG_SZ /d \"{shellValue}\" /f";
        var psi = new ProcessStartInfo("reg.exe", arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(psi);
            process!.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC prompt declined.
            return false;
        }
    }
}
