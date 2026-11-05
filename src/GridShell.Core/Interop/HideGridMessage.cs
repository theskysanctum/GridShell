using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Posted to GridShell.Grid when the user activates an already-running window from
/// the taskbar's own window list while the grid happens to be open — unlike
/// <see cref="GridToggleMessage"/>, this unconditionally hides (never shows) the grid, since the
/// taskbar has no way to know the grid's current visibility and toggling could incorrectly
/// re-show it.</summary>
public static class HideGridMessage
{
    public static readonly uint Id = RegisterWindowMessage("GridShellHideGrid");
}
