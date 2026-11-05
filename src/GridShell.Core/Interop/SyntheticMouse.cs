using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Sends synthetic relative mouse movement and button clicks — used to drive the
/// desktop cursor from a controller's right stick and triggers.</summary>
public static class SyntheticMouse
{
    public static void Move(int dx, int dy) => Send(new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF.MOUSEEVENTF_MOVE });

    public static void LeftDown() => Send(new MOUSEINPUT { dwFlags = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN });

    public static void LeftUp() => Send(new MOUSEINPUT { dwFlags = MOUSEEVENTF.MOUSEEVENTF_LEFTUP });

    public static void RightDown() => Send(new MOUSEINPUT { dwFlags = MOUSEEVENTF.MOUSEEVENTF_RIGHTDOWN });

    public static void RightUp() => Send(new MOUSEINPUT { dwFlags = MOUSEEVENTF.MOUSEEVENTF_RIGHTUP });

    private static void Send(MOUSEINPUT mi)
    {
        var input = new INPUT { type = INPUTTYPE.INPUT_MOUSE, mi = mi };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
