using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>The custom window message the taskbar process posts to the pre-warmed, hidden
/// GridShell.Grid process to tell it to show/hide itself — used instead of starting/killing
/// that process on every toggle, which is what caused a visible delay opening the grid.</summary>
public static class GridToggleMessage
{
    public static readonly uint Id = RegisterWindowMessage("GridShellToggleGrid");
}
