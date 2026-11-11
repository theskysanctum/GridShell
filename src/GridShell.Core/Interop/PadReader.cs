using System.Runtime.InteropServices;
using Windows.Gaming.Input;

namespace GridShell.Core.Interop;

[Flags]
public enum PadButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    View = 1 << 4,
    Menu = 1 << 5,
    LeftThumbstick = 1 << 6,
    RightThumbstick = 1 << 7,
    LeftShoulder = 1 << 8,
    RightShoulder = 1 << 9,
    DPadUp = 1 << 10,
    DPadDown = 1 << 11,
    DPadLeft = 1 << 12,
    DPadRight = 1 << 13,
}

public readonly record struct PadReading(
    PadButtons Buttons,
    double LeftX,
    double LeftY,
    double RightX,
    double RightY,
    double LeftTrigger,
    double RightTrigger);

public static class PadReader
{
    private static readonly object Gate = new();
    private static readonly TimeSpan MinReadInterval = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<uint, IntPtr> Open = new();
    private static bool _sdlTried;
    private static bool _sdlReady;
    private static DateTime _lastRead = DateTime.MinValue;
    private static DateTime _lastScan = DateTime.MinValue;
    private static PadReading? _cached;

    public static PadReading? Read()
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (now - _lastRead < MinReadInterval)
            {
                return _cached;
            }

            _lastRead = now;
            _cached = ReadSdl(now) ?? ReadWindows();
            return _cached;
        }
    }

    private static PadReading? ReadWindows()
    {
        var gamepad = Gamepad.Gamepads.FirstOrDefault();
        if (gamepad is null)
        {
            return null;
        }

        var r = gamepad.GetCurrentReading();
        var b = PadButtons.None;
        Map(r.Buttons, GamepadButtons.A, PadButtons.A, ref b);
        Map(r.Buttons, GamepadButtons.B, PadButtons.B, ref b);
        Map(r.Buttons, GamepadButtons.X, PadButtons.X, ref b);
        Map(r.Buttons, GamepadButtons.Y, PadButtons.Y, ref b);
        Map(r.Buttons, GamepadButtons.View, PadButtons.View, ref b);
        Map(r.Buttons, GamepadButtons.Menu, PadButtons.Menu, ref b);
        Map(r.Buttons, GamepadButtons.LeftThumbstick, PadButtons.LeftThumbstick, ref b);
        Map(r.Buttons, GamepadButtons.RightThumbstick, PadButtons.RightThumbstick, ref b);
        Map(r.Buttons, GamepadButtons.LeftShoulder, PadButtons.LeftShoulder, ref b);
        Map(r.Buttons, GamepadButtons.RightShoulder, PadButtons.RightShoulder, ref b);
        Map(r.Buttons, GamepadButtons.DPadUp, PadButtons.DPadUp, ref b);
        Map(r.Buttons, GamepadButtons.DPadDown, PadButtons.DPadDown, ref b);
        Map(r.Buttons, GamepadButtons.DPadLeft, PadButtons.DPadLeft, ref b);
        Map(r.Buttons, GamepadButtons.DPadRight, PadButtons.DPadRight, ref b);
        return new PadReading(
            b,
            r.LeftThumbstickX,
            r.LeftThumbstickY,
            r.RightThumbstickX,
            r.RightThumbstickY,
            r.LeftTrigger,
            r.RightTrigger);
    }

    private static void Map(GamepadButtons source, GamepadButtons flag, PadButtons target, ref PadButtons result)
    {
        if (source.HasFlag(flag))
        {
            result |= target;
        }
    }

    private static PadReading? ReadSdl(DateTime now)
    {
        if (!EnsureSdl())
        {
            return null;
        }

        try
        {
            if (now - _lastScan >= RescanInterval)
            {
                _lastScan = now;
                Rescan();
            }

            SDL_UpdateGamepads();

            var buttons = PadButtons.None;
            double lx = 0, ly = 0, rx = 0, ry = 0, lt = 0, rt = 0;
            var any = false;

            foreach (var pad in Open.Values)
            {
                any = true;
                if (Btn(pad, 0)) buttons |= PadButtons.A;
                if (Btn(pad, 1)) buttons |= PadButtons.B;
                if (Btn(pad, 2)) buttons |= PadButtons.X;
                if (Btn(pad, 3)) buttons |= PadButtons.Y;
                if (Btn(pad, 4)) buttons |= PadButtons.View;
                if (Btn(pad, 6)) buttons |= PadButtons.Menu;
                if (Btn(pad, 7)) buttons |= PadButtons.LeftThumbstick;
                if (Btn(pad, 8)) buttons |= PadButtons.RightThumbstick;
                if (Btn(pad, 9)) buttons |= PadButtons.LeftShoulder;
                if (Btn(pad, 10)) buttons |= PadButtons.RightShoulder;
                if (Btn(pad, 11)) buttons |= PadButtons.DPadUp;
                if (Btn(pad, 12)) buttons |= PadButtons.DPadDown;
                if (Btn(pad, 13)) buttons |= PadButtons.DPadLeft;
                if (Btn(pad, 14)) buttons |= PadButtons.DPadRight;

                Strongest(ref lx, Axis(pad, 0));
                Strongest(ref ly, -Axis(pad, 1));
                Strongest(ref rx, Axis(pad, 2));
                Strongest(ref ry, -Axis(pad, 3));
                lt = Math.Max(lt, Math.Max(0, Axis(pad, 4)));
                rt = Math.Max(rt, Math.Max(0, Axis(pad, 5)));
            }

            return any ? new PadReading(buttons, lx, ly, rx, ry, lt, rt) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _sdlReady = false;
            return null;
        }
    }

    private static bool Btn(IntPtr pad, int button) => SDL_GetGamepadButton(pad, button);

    private static double Axis(IntPtr pad, int axis)
    {
        var raw = SDL_GetGamepadAxis(pad, axis);
        return raw < 0 ? raw / 32768.0 : raw / 32767.0;
    }

    private static void Strongest(ref double current, double candidate)
    {
        if (Math.Abs(candidate) > Math.Abs(current))
        {
            current = candidate;
        }
    }

    private static bool EnsureSdl()
    {
        if (_sdlTried)
        {
            return _sdlReady;
        }

        _sdlTried = true;
        try
        {
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH2", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_JOY_CONS", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_COMBINE_JOY_CONS", "1");
            _sdlReady = SDL_Init(SdlInitGamepad);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _sdlReady = false;
        }

        return _sdlReady;
    }

    private static void Rescan()
    {
        var ids = SDL_GetGamepads(out var count);
        var present = new HashSet<uint>();
        if (ids != IntPtr.Zero)
        {
            for (var i = 0; i < count; i++)
            {
                present.Add((uint)Marshal.ReadInt32(ids, i * sizeof(uint)));
            }

            SDL_free(ids);
        }

        foreach (var id in present)
        {
            if (!Open.ContainsKey(id))
            {
                var pad = SDL_OpenGamepad(id);
                if (pad != IntPtr.Zero)
                {
                    Open[id] = pad;
                }
            }
        }

        foreach (var id in Open.Keys.ToList())
        {
            if (!present.Contains(id) || !SDL_GamepadConnected(Open[id]))
            {
                SDL_CloseGamepad(Open[id]);
                Open.Remove(id);
            }
        }
    }

    private const uint SdlInitGamepad = 0x00002000u;
    private const string Lib = "SDL3";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_Init(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_UpdateGamepads();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetGamepads(out int count);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_OpenGamepad(uint instanceId);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_CloseGamepad(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_GamepadConnected(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_GetGamepadButton(IntPtr gamepad, int button);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern short SDL_GetGamepadAxis(IntPtr gamepad, int axis);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_free(IntPtr mem);
}
