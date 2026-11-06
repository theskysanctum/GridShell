using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Claims Shift+Print Screen via RegisterHotKey - the mouse/keyboard-only equivalent
/// of the controller's Select+B, opening the same Window/Snippet/Fullscreen picker instead of
/// jumping straight to one capture mode (that's what bare PrintScreenHotkey is for).</summary>
public static class ScreenshotMenuHotkey
{
    private const int VK_SNAPSHOT = 0x2C;
    public const int HotkeyId = 0xA5A6; // one past PrintScreenHotkey's id, same arbitrary scheme

    public static bool Register(HWND hwnd) =>
        RegisterHotKey(hwnd, HotkeyId, HotKeyModifiers.MOD_SHIFT, VK_SNAPSHOT);

    public static void Unregister(HWND hwnd) => UnregisterHotKey(hwnd, HotkeyId);
}
