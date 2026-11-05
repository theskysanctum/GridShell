using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Claims the Print Screen key via RegisterHotKey, not a WH_KEYBOARD_LL hook -
/// confirmed empirically that VK_SNAPSHOT never reaches a low-level keyboard hook at all,
/// since Windows' own built-in "copy screen to clipboard" behavior consumes it below the level
/// hooks observe. RegisterHotKey is the API actually meant for claiming a system-reserved key
/// like this; it delivers WM_HOTKEY to the registering window instead.</summary>
public static class PrintScreenHotkey
{
    private const int VK_SNAPSHOT = 0x2C;
    public const int HotkeyId = 0xA5A5; // arbitrary, unlikely to collide with anything else

    public static bool Register(HWND hwnd) =>
        RegisterHotKey(hwnd, HotkeyId, HotKeyModifiers.MOD_NONE, VK_SNAPSHOT);

    public static void Unregister(HWND hwnd) => UnregisterHotKey(hwnd, HotkeyId);
}
