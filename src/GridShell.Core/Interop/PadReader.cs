using System.IO;
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

    /// <summary>The DualShock4/DualSense touchpad's physical click (a real button, distinct
    /// from touch presence/position - SDL_GAMEPAD_BUTTON_TOUCHPAD, ordinal 20 in SDL's own
    /// SDL_GamepadButton enum).</summary>
    TouchpadClick = 1 << 14,
}

public readonly record struct PadReading(
    PadButtons Buttons,
    double LeftX,
    double LeftY,
    double RightX,
    double RightY,
    double LeftTrigger,
    double RightTrigger);

/// <summary>One currently-connected gamepad, for callers (Controller Manager) that need to
/// address a specific device rather than PadReader's own merged-across-all-pads Read().</summary>
public readonly record struct PadDevice(uint Id, string Name, ushort VendorId, ushort ProductId);

/// <summary>Mirrors SDL3's SDL_SensorType ordinals exactly (SDL_sensor.h) - only the two
/// Controller Manager actually exposes (gyro/accel); the Joy-Con-half-specific values aren't
/// surfaced since GridShell always reads combined Joy-Cons as one pad (SDL_JOYSTICK_HIDAPI_
/// COMBINE_JOY_CONS, already set in EnsureSdl).</summary>
public enum PadSensorType
{
    Accel = 1,
    Gyro = 2,
}

public readonly record struct PadSensorSample(float X, float Y, float Z);

public readonly record struct PadTouchpadFinger(bool Down, float X, float Y, float Pressure);

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

    /// <summary>Raised on this process's own SDL instance the moment a gamepad is newly opened
    /// (first seen, or replugged) - each process (GridShell, GridShell.Grid) has its own
    /// independent PadReader/SDL state, so anything that needs to react in both (e.g.
    /// re-applying a saved LED color/trigger mode) must subscribe from each process's own
    /// startup, not just one.</summary>
    public static event Action<PadDevice>? DeviceConnected;

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
                if (Btn(pad, 20)) buttons |= PadButtons.TouchpadClick;

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

    /// <summary>Lists every currently-open SDL gamepad by its own identity, for callers that need
    /// to address one specific device (Controller Manager) rather than the merged-across-all-pads
    /// Read() navigation uses.</summary>
    public static IReadOnlyList<PadDevice> ListDevices()
    {
        lock (Gate)
        {
            if (!EnsureSdl())
            {
                return Array.Empty<PadDevice>();
            }

            var now = DateTime.UtcNow;
            if (now - _lastScan >= RescanInterval)
            {
                _lastScan = now;
                Rescan();
            }

            return Open.Select(kv => new PadDevice(
                kv.Key,
                Marshal.PtrToStringUTF8(SDL_GetGamepadName(kv.Value)) ?? "Unknown Controller",
                SDL_GetGamepadVendor(kv.Value),
                SDL_GetGamepadProduct(kv.Value))).ToList();
        }
    }

    /// <summary>Reads one specific device by the id ListDevices() returned, rather than the
    /// merged reading across all open pads that Read() returns.</summary>
    public static PadReading? ReadDevice(uint id)
    {
        lock (Gate)
        {
            if (!Open.TryGetValue(id, out var pad))
            {
                return null;
            }

            SDL_UpdateGamepads();

            var buttons = PadButtons.None;
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
            if (Btn(pad, 20)) buttons |= PadButtons.TouchpadClick;

            return new PadReading(
                buttons,
                Axis(pad, 0),
                -Axis(pad, 1),
                Axis(pad, 2),
                -Axis(pad, 3),
                Math.Max(0, Axis(pad, 4)),
                Math.Max(0, Axis(pad, 5)));
        }
    }

    public static bool SetLed(uint id, byte red, byte green, byte blue)
    {
        lock (Gate)
        {
            return Open.TryGetValue(id, out var pad) && SDL_SetGamepadLED(pad, red, green, blue);
        }
    }

    public static bool Rumble(uint id, ushort lowFrequency, ushort highFrequency, uint durationMs)
    {
        lock (Gate)
        {
            return Open.TryGetValue(id, out var pad) && SDL_RumbleGamepad(pad, lowFrequency, highFrequency, durationMs);
        }
    }

    public static bool RumbleTriggers(uint id, ushort left, ushort right, uint durationMs)
    {
        lock (Gate)
        {
            return Open.TryGetValue(id, out var pad) && SDL_RumbleGamepadTriggers(pad, left, right, durationMs);
        }
    }

    public static bool HasSensor(uint id, PadSensorType type)
    {
        lock (Gate)
        {
            return Open.TryGetValue(id, out var pad) && SDL_GamepadHasSensor(pad, (int)type);
        }
    }

    public static bool SetSensorEnabled(uint id, PadSensorType type, bool enabled)
    {
        lock (Gate)
        {
            return Open.TryGetValue(id, out var pad) && SDL_SetGamepadSensorEnabled(pad, (int)type, enabled);
        }
    }

    public static PadSensorSample? ReadSensor(uint id, PadSensorType type)
    {
        lock (Gate)
        {
            if (!Open.TryGetValue(id, out var pad))
            {
                return null;
            }

            var data = new float[3];
            return SDL_GetGamepadSensorData(pad, (int)type, data, 3) ? new PadSensorSample(data[0], data[1], data[2]) : null;
        }
    }

    public static int TouchpadCount(uint id)
    {
        lock (Gate)
        {
            return Open.TryGetValue(id, out var pad) ? SDL_GetNumGamepadTouchpads(pad) : 0;
        }
    }

    public static PadTouchpadFinger? ReadTouchpadFinger(uint id, int touchpad, int finger)
    {
        lock (Gate)
        {
            if (!Open.TryGetValue(id, out var pad))
            {
                return null;
            }

            return SDL_GetGamepadTouchpadFinger(pad, touchpad, finger, out var down, out var x, out var y, out var pressure)
                ? new PadTouchpadFinger(down, x, y, pressure)
                : null;
        }
    }

    /// <summary>Raw output-report passthrough (e.g. DualSense adaptive trigger effect bytes) -
    /// SDL does not interpret or validate this data, it hands it straight to the device's own
    /// HID write path, so the caller is responsible for building a correct report.</summary>
    public static bool SendEffect(uint id, byte[] data)
    {
        lock (Gate)
        {
            return Open.TryGetValue(id, out var pad) && SDL_SendGamepadEffect(pad, data, data.Length);
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

            // SDL's own built-in gamepad mappings only cover Nintendo's own Switch 2 hardware
            // (confirmed: SDL_hidapi_switch2.c gates on USB_VENDOR_NINTENDO), so licensed
            // third-party pads like PowerA's Switch 2 controllers enumerate as a plain joystick
            // (SDL_IsGamepad false) with no button/axis mapping at all. The community-maintained
            // SDL_GameControllerDB (bundled below, zlib-licensed) carries a mapping for exactly
            // that device and many others; loading it widens real-world compatibility well
            // beyond what SDL ships out of the box.
            var mappingsPath = Path.Combine(AppContext.BaseDirectory, "GameControllerDB", "gamecontrollerdb.txt");
            if (_sdlReady && File.Exists(mappingsPath))
            {
                SDL_AddGamepadMappingsFromFile(mappingsPath);
            }
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
                    DeviceConnected?.Invoke(new PadDevice(
                        id,
                        Marshal.PtrToStringUTF8(SDL_GetGamepadName(pad)) ?? "Unknown Controller",
                        SDL_GetGamepadVendor(pad),
                        SDL_GetGamepadProduct(pad)));
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

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_AddGamepadMappingsFromFile([MarshalAs(UnmanagedType.LPUTF8Str)] string file);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetGamepadName(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ushort SDL_GetGamepadVendor(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ushort SDL_GetGamepadProduct(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SetGamepadLED(IntPtr gamepad, byte red, byte green, byte blue);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_RumbleGamepad(IntPtr gamepad, ushort lowFrequencyRumble, ushort highFrequencyRumble, uint durationMs);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_RumbleGamepadTriggers(IntPtr gamepad, ushort leftRumble, ushort rightRumble, uint durationMs);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_GamepadHasSensor(IntPtr gamepad, int sensorType);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SetGamepadSensorEnabled(IntPtr gamepad, int sensorType, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_GetGamepadSensorData(IntPtr gamepad, int sensorType, [Out] float[] data, int numValues);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetNumGamepadTouchpads(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_GetGamepadTouchpadFinger(
        IntPtr gamepad, int touchpad, int finger,
        [MarshalAs(UnmanagedType.U1)] out bool down, out float x, out float y, out float pressure);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SendGamepadEffect(IntPtr gamepad, [In] byte[] data, int size);
}
