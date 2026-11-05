using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>The custom window message the always-running taskbar process posts to GridShell.Grid
/// (which hosts the on-screen keyboard, already pre-warmed and themed) when Y/Triangle is
/// pressed anywhere — the keyboard's trigger is global like the Menu button's, not scoped to
/// GridShell's own windows, so it lives in the taskbar's always-on controller polling rather
/// than the grid's own (which only runs while the grid is visible).</summary>
public static class OpenKeyboardMessage
{
    public static readonly uint Id = RegisterWindowMessage("GridShellOpenKeyboard");
}
