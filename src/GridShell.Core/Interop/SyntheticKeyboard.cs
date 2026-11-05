using System.Linq;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Sends a synthetic key down+up for a virtual-key code — used to drive controller
/// navigation through GridShell's existing, already-working keyboard handling (arrow-key grid
/// navigation, Enter to launch, Escape to back out) rather than reimplementing it.</summary>
public static class SyntheticKeyboard
{
    public static void PressKey(VK vk)
    {
        var down = new INPUT
        {
            type = INPUTTYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = vk },
        };

        var up = new INPUT
        {
            type = INPUTTYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF.KEYEVENTF_KEYUP },
        };

        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Presses a virtual-key while holding one or more modifier keys down around it —
    /// e.g. Ctrl+C. Unlike TypeChar, this goes through real virtual-key codes (required for a
    /// modifier combo to register as a shortcut rather than literal text), so it only works for
    /// characters with a known VK (see OnScreenKeyboardWindow's char-to-VK mapping).</summary>
    public static void PressKeyWithModifiers(VK vk, IReadOnlyList<VK> modifiers)
    {
        var inputs = new List<INPUT>(modifiers.Count * 2 + 2);
        inputs.AddRange(modifiers.Select(mod => new INPUT
        {
            type = INPUTTYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = mod },
        }));

        inputs.Add(new INPUT { type = INPUTTYPE.INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk } });
        inputs.Add(new INPUT
        {
            type = INPUTTYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF.KEYEVENTF_KEYUP },
        });

        for (var i = modifiers.Count - 1; i >= 0; i--)
        {
            inputs.Add(new INPUT
            {
                type = INPUTTYPE.INPUT_KEYBOARD,
                ki = new KEYBDINPUT { wVk = modifiers[i], dwFlags = KEYEVENTF.KEYEVENTF_KEYUP },
            });
        }

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <summary>Types an arbitrary Unicode character into whatever has keyboard focus — unlike
    /// PressKey, this doesn't need a virtual-key code per character (which would require
    /// per-layout letter/shift mapping), so it's what the on-screen keyboard uses to type real
    /// text system-wide.</summary>
    public static void TypeChar(char c)
    {
        var down = new INPUT
        {
            type = INPUTTYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF.KEYEVENTF_UNICODE },
        };

        var up = new INPUT
        {
            type = INPUTTYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF.KEYEVENTF_UNICODE | KEYEVENTF.KEYEVENTF_KEYUP },
        };

        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }
}
