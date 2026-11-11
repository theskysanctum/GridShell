using System.Linq;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using GridShell.Core.Interop;

namespace GridShell.Core.Controllers;

/// <summary>Thin wrapper around Nefarius.ViGEm.Client, translating a PadReader-shaped
/// PadReading into a virtual Xbox360 controller so apps that only understand XInput can see a
/// device PadReader can already read (SDL/libusb) but XInput never recognized. The ViGEmBus
/// driver is a separate install from this NuGet package and commonly missing/broken -
/// VigemBusNotFoundException is the expected, user-actionable failure mode, not a bug.</summary>
/// <summary>Xbox360Button isn't a real C# enum (Nefarius.ViGEm.Client models it as a class with
/// static readonly instances, one distinct subtype per button, confirmed via reflection -
/// Enum.GetValues/TryParse don't apply to it), so this is the Enum.GetValues/TryParse
/// equivalent for it, built once via reflection over its public static fields.</summary>
public static class Xbox360ButtonCatalog
{
    public static readonly IReadOnlyList<Xbox360Button> All = typeof(Xbox360Button)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(f => (Xbox360Button)f.GetValue(null)!)
        .ToList();

    private static readonly Dictionary<string, Xbox360Button> ByName =
        All.ToDictionary(b => b.ToString()!, b => b);

    public static bool TryParse(string name, out Xbox360Button value) => ByName.TryGetValue(name, out value!);
}

public sealed class VirtualPadBridge : IDisposable
{
    /// <summary>Every source button's default target - callers only need to specify entries in
    /// a remap dictionary that actually differ from this.</summary>
    public static readonly IReadOnlyDictionary<PadButtons, Xbox360Button> DefaultButtonMap = new Dictionary<PadButtons, Xbox360Button>
    {
        [PadButtons.A] = Xbox360Button.A,
        [PadButtons.B] = Xbox360Button.B,
        [PadButtons.X] = Xbox360Button.X,
        [PadButtons.Y] = Xbox360Button.Y,
        [PadButtons.View] = Xbox360Button.Back,
        [PadButtons.Menu] = Xbox360Button.Start,
        [PadButtons.LeftThumbstick] = Xbox360Button.LeftThumb,
        [PadButtons.RightThumbstick] = Xbox360Button.RightThumb,
        [PadButtons.LeftShoulder] = Xbox360Button.LeftShoulder,
        [PadButtons.RightShoulder] = Xbox360Button.RightShoulder,
        [PadButtons.DPadUp] = Xbox360Button.Up,
        [PadButtons.DPadDown] = Xbox360Button.Down,
        [PadButtons.DPadLeft] = Xbox360Button.Left,
        [PadButtons.DPadRight] = Xbox360Button.Right,
        [PadButtons.TouchpadClick] = Xbox360Button.Guide,
    };

    private readonly ViGEmClient _client;
    private readonly IXbox360Controller _pad;
    private readonly Dictionary<PadButtons, Xbox360Button> _buttonMap;

    /// <summary>Throws VigemBusNotFoundException if the ViGEmBus driver isn't installed/running -
    /// callers should catch this specifically and point the user at installing it, not treat it
    /// as an unexpected crash. buttonRemap overrides only the source buttons it contains;
    /// everything else keeps DefaultButtonMap's identity-ish mapping.</summary>
    public VirtualPadBridge(IReadOnlyDictionary<PadButtons, Xbox360Button>? buttonRemap = null)
    {
        _client = new ViGEmClient();
        _pad = _client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false;
        _pad.Connect();

        _buttonMap = new Dictionary<PadButtons, Xbox360Button>(DefaultButtonMap);
        if (buttonRemap is not null)
        {
            foreach (var (source, target) in buttonRemap)
            {
                _buttonMap[source] = target;
            }
        }
    }

    public void Feed(PadReading reading)
    {
        _pad.ResetReport();

        foreach (var (source, target) in _buttonMap)
        {
            _pad.SetButtonState(target, reading.Buttons.HasFlag(source));
        }

        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxis(reading.LeftX));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxis(reading.LeftY));
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, ToAxis(reading.RightX));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, ToAxis(reading.RightY));
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, ToSlider(reading.LeftTrigger));
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, ToSlider(reading.RightTrigger));

        _pad.SubmitReport();
    }

    private static short ToAxis(double value) =>
        (short)Math.Clamp(value * (value < 0 ? 32768.0 : 32767.0), short.MinValue, short.MaxValue);

    private static byte ToSlider(double value) =>
        (byte)Math.Clamp(value * 255.0, 0, 255);

    public void Dispose()
    {
        _pad.Disconnect();
        _client.Dispose();
    }
}
