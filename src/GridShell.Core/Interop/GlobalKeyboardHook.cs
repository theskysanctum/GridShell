using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Watches for a bare tap of the Windows key (down and up with no other key pressed
/// in between) and swallows only that tap, so the native Start menu never opens. Any Win+X
/// combo (Win+E, Win+D, Win+L, ...) is left completely untouched.</summary>
public sealed class GlobalKeyboardHook : IDisposable
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    // Keep the delegate alive for the hook's lifetime — otherwise the GC can collect it
    // while Windows still holds a native pointer to it, crashing the process.
    private readonly HookProc _proc;
    private HHOOK _hookId = HHOOK.NULL;
    private bool _winKeyDown;
    private bool _otherKeyPressedDuringWinHold;

    public event Action? WinKeyTapped;

    /// <summary>Gates WinKeyTapped/the native-Start-menu swallow - checked here rather than by
    /// not installing the hook at all, so toggling it at runtime (Settings) takes effect
    /// immediately without a Start()/Stop() cycle.</summary>
    public bool WinKeyHandlingEnabled { get; set; } = true;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (!_hookId.IsNull)
        {
            return;
        }

        using var curModule = System.Diagnostics.Process.GetCurrentProcess().MainModule!;
        _hookId = SetWindowsHookEx(HookType.WH_KEYBOARD_LL, _proc, Kernel32.GetModuleHandle(curModule.ModuleName), 0);
    }

    public void Stop()
    {
        if (_hookId.IsNull)
        {
            return;
        }

        UnhookWindowsHookEx(_hookId);
        _hookId = HHOOK.NULL;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && WinKeyHandlingEnabled)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)data.vkCode;
            var isWinKey = vkCode == VK_LWIN || vkCode == VK_RWIN;
            var message = (WindowMessage)(uint)wParam;

            if (isWinKey)
            {
                if (message is WindowMessage.WM_KEYDOWN or WindowMessage.WM_SYSKEYDOWN)
                {
                    if (!_winKeyDown)
                    {
                        _winKeyDown = true;
                        _otherKeyPressedDuringWinHold = false;
                    }
                }
                else if (message is WindowMessage.WM_KEYUP or WindowMessage.WM_SYSKEYUP)
                {
                    if (_winKeyDown)
                    {
                        _winKeyDown = false;
                        if (!_otherKeyPressedDuringWinHold)
                        {
                            WinKeyTapped?.Invoke();

                            // We're blocking the real key-up below, so the OS's own notion of
                            // "is Win still held" never gets cleared — without this, the very
                            // next key the user presses gets treated as Win+<key> (e.g. Win+E).
                            // Tell it the key is up ourselves.
                            SendSyntheticKeyUp((ushort)vkCode);

                            return (IntPtr)1; // swallow: don't let the native Start menu open
                        }
                    }
                }
            }
            else if (_winKeyDown && message is WindowMessage.WM_KEYDOWN or WindowMessage.WM_SYSKEYDOWN)
            {
                _otherKeyPressedDuringWinHold = true;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void SendSyntheticKeyUp(ushort vk)
    {
        var input = new INPUT
        {
            type = INPUTTYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = (VK)vk,
                dwFlags = KEYEVENTF.KEYEVENTF_KEYUP,
            },
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() => Stop();
}
