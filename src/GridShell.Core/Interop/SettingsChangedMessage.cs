using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>The custom window message Settings (running inside GridShell.Grid) posts to the
/// taskbar process whenever a setting changes, so the taskbar — a separate, always-running
/// process with its own copy of AppSettings loaded at startup — can reload and re-apply it
/// live (e.g. a theme change) instead of only taking effect on its next restart.</summary>
public static class SettingsChangedMessage
{
    public static readonly uint Id = RegisterWindowMessage("GridShellSettingsChanged");
}
