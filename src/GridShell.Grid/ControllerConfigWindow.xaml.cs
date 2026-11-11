using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using GridShell.Core.Controllers;
using GridShell.Core.Interop;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GridShell;

/// <summary>Per-device configuration - remap, LED, rumble test, gyro/accel readout, touchpad,
/// and experimental adaptive triggers. Reads/writes ControllerPreferencesStore, keyed by this
/// device's VID:PID, so settings survive restarts and get re-applied the next time a virtual
/// pad is created for this device (see ControllerManagerWindow/ControllerEntry).</summary>
public partial class ControllerConfigWindow : Window, INotifyPropertyChanged
{
    private readonly PadDevice _device;
    private readonly string _prefsKey;
    private readonly Dictionary<string, ControllerPreferences> _allPrefs;
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string DeviceName => $"{_device.Name} (VID 0x{_device.VendorId:X4} / PID 0x{_device.ProductId:X4})";

    public List<ConfigCategory> Categories { get; } = Enum.GetValues<ConfigCategory>().ToList();

    private ConfigCategory _selectedCategory = ConfigCategory.Remap;
    public ConfigCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRemapSelected));
            OnPropertyChanged(nameof(IsLedSelected));
            OnPropertyChanged(nameof(IsRumbleSelected));
            OnPropertyChanged(nameof(IsGyroAccelSelected));
            OnPropertyChanged(nameof(IsTouchpadSelected));
            OnPropertyChanged(nameof(IsAdaptiveTriggersSelected));
        }
    }

    public bool IsRemapSelected => SelectedCategory == ConfigCategory.Remap;
    public bool IsLedSelected => SelectedCategory == ConfigCategory.Led;
    public bool IsRumbleSelected => SelectedCategory == ConfigCategory.Rumble;
    public bool IsGyroAccelSelected => SelectedCategory == ConfigCategory.GyroAccel;
    public bool IsTouchpadSelected => SelectedCategory == ConfigCategory.Touchpad;
    public bool IsAdaptiveTriggersSelected => SelectedCategory == ConfigCategory.AdaptiveTriggers;

    public ObservableCollection<RemapRow> RemapRows { get; } = new();

    private byte _ledRed, _ledGreen, _ledBlue;
    public byte LedRed { get => _ledRed; set { _ledRed = value; OnPropertyChanged(); } }
    public byte LedGreen { get => _ledGreen; set { _ledGreen = value; OnPropertyChanged(); } }
    public byte LedBlue { get => _ledBlue; set { _ledBlue = value; OnPropertyChanged(); } }

    public bool HasGyro { get; }
    public bool HasAccel { get; }
    public Visibility NoSensorsVisibility => (HasGyro || HasAccel) ? Visibility.Collapsed : Visibility.Visible;

    private bool _gyroEnabled;
    public bool GyroEnabled
    {
        get => _gyroEnabled;
        set { _gyroEnabled = value; PadReader.SetSensorEnabled(_device.Id, PadSensorType.Gyro, value); OnPropertyChanged(); }
    }

    private bool _accelEnabled;
    public bool AccelEnabled
    {
        get => _accelEnabled;
        set { _accelEnabled = value; PadReader.SetSensorEnabled(_device.Id, PadSensorType.Accel, value); OnPropertyChanged(); }
    }

    private string _gyroReadout = "";
    public string GyroReadout { get => _gyroReadout; set { _gyroReadout = value; OnPropertyChanged(); } }

    private string _accelReadout = "";
    public string AccelReadout { get => _accelReadout; set { _accelReadout = value; OnPropertyChanged(); } }

    public bool HasTouchpad { get; }
    public Visibility HasTouchpadVisibility => HasTouchpad ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoTouchpadVisibility => HasTouchpad ? Visibility.Collapsed : Visibility.Visible;

    public List<TouchpadClickMode> TouchpadClickModeOptions { get; } = Enum.GetValues<TouchpadClickMode>().ToList();
    public TouchpadClickMode TouchpadClickMode { get; set; } = TouchpadClickMode.Off;

    private double _touchpadSensitivity = 1.0;
    public double TouchpadSensitivity { get => _touchpadSensitivity; set { _touchpadSensitivity = value; OnPropertyChanged(); } }

    private string _touchpadReadout = "";
    public string TouchpadReadout { get => _touchpadReadout; set { _touchpadReadout = value; OnPropertyChanged(); } }

    public List<TriggerEffectMode> TriggerModeOptions { get; } = Enum.GetValues<TriggerEffectMode>().ToList();
    public TriggerEffectMode RightTriggerMode { get; set; } = TriggerEffectMode.Off;
    public TriggerEffectMode LeftTriggerMode { get; set; } = TriggerEffectMode.Off;

    public ControllerConfigWindow(PadDevice device)
    {
        InitializeComponent();
        DataContext = this;
        this.HideWindowIcon();

        _device = device;
        _prefsKey = ControllerPreferencesStore.KeyFor(device.VendorId, device.ProductId);
        _allPrefs = ControllerPreferencesStore.Load();
        var prefs = _allPrefs.GetValueOrDefault(_prefsKey) ?? new ControllerPreferences();

        foreach (var (source, defaultTarget) in VirtualPadBridge.DefaultButtonMap)
        {
            var target = defaultTarget;
            if (prefs.ButtonRemap.TryGetValue(source.ToString(), out var savedTargetName) &&
                GridShell.Core.Controllers.Xbox360ButtonCatalog.TryParse(savedTargetName, out var savedTarget))
            {
                target = savedTarget;
            }

            RemapRows.Add(new RemapRow(source, target));
        }

        LedRed = prefs.LedRed ?? 0;
        LedGreen = prefs.LedGreen ?? 0;
        LedBlue = prefs.LedBlue ?? 0;
        TouchpadClickMode = prefs.TouchpadClickMode;
        TouchpadSensitivity = prefs.TouchpadSensitivity;

        HasGyro = PadReader.HasSensor(device.Id, PadSensorType.Gyro);
        HasAccel = PadReader.HasSensor(device.Id, PadSensorType.Accel);
        HasTouchpad = PadReader.TouchpadCount(device.Id) > 0;

        if (HasGyro && prefs.GyroEnabled)
        {
            GyroEnabled = true;
        }

        if (HasAccel && prefs.AccelEnabled)
        {
            AccelEnabled = true;
        }

        _liveTimer.Tick += (_, _) => UpdateLiveReadouts();
        _liveTimer.Start();
        Closed += (_, _) => _liveTimer.Stop();
    }

    private void UpdateLiveReadouts()
    {
        if (HasGyro && GyroEnabled && PadReader.ReadSensor(_device.Id, PadSensorType.Gyro) is { } gyro)
        {
            GyroReadout = $"X: {gyro.X:F2}  Y: {gyro.Y:F2}  Z: {gyro.Z:F2} rad/s";
        }

        if (HasAccel && AccelEnabled && PadReader.ReadSensor(_device.Id, PadSensorType.Accel) is { } accel)
        {
            AccelReadout = $"X: {accel.X:F2}  Y: {accel.Y:F2}  Z: {accel.Z:F2} m/s²";
        }

        // Click emulation itself is handled continuously by ControllerMouseInput (reads the
        // same saved preference), not here - this window only shows a live readout, so clicks
        // aren't double-fired by two separate pollers.
        if (HasTouchpad && PadReader.ReadTouchpadFinger(_device.Id, 0, 0) is { } finger)
        {
            TouchpadReadout = finger.Down
                ? $"Down at ({finger.X:F2}, {finger.Y:F2}), pressure {finger.Pressure:F2}"
                : "Not touched";
        }
    }

    /// <summary>LED and trigger effects are hardware state the controller itself doesn't
    /// remember across a replug/app restart (unlike a virtual pad, which only exists while
    /// GridShell is feeding it) - "Apply" here both sends it live AND persists it immediately,
    /// so it's still there the next time this device connects (see ControllerAutoApply), rather
    /// than only taking effect if the user also remembers to click this window's own Save.</summary>
    private void ApplyLed_Click(object sender, RoutedEventArgs e)
    {
        PadReader.SetLed(_device.Id, LedRed, LedGreen, LedBlue);
        UpdateSavedPreferences(prefs =>
        {
            prefs.LedRed = LedRed;
            prefs.LedGreen = LedGreen;
            prefs.LedBlue = LedBlue;
        });
    }

    private void TestRumble_Click(object sender, RoutedEventArgs e) => PadReader.Rumble(_device.Id, 32000, 32000, 500);

    private void ApplyTriggers_Click(object sender, RoutedEventArgs e)
    {
        ControllerAutoApply.ApplyTriggerMode(_device.Id, rightTrigger: true, RightTriggerMode);
        ControllerAutoApply.ApplyTriggerMode(_device.Id, rightTrigger: false, LeftTriggerMode);
        UpdateSavedPreferences(prefs =>
        {
            prefs.RightTriggerMode = RightTriggerMode;
            prefs.LeftTriggerMode = LeftTriggerMode;
        });
    }

    private void UpdateSavedPreferences(Action<ControllerPreferences> update)
    {
        var prefs = _allPrefs.GetValueOrDefault(_prefsKey) ?? new ControllerPreferences();
        update(prefs);
        _allPrefs[_prefsKey] = prefs;
        ControllerPreferencesStore.Save(_allPrefs);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdateSavedPreferences(prefs =>
        {
            prefs.LedRed = LedRed;
            prefs.LedGreen = LedGreen;
            prefs.LedBlue = LedBlue;
            prefs.TouchpadClickMode = TouchpadClickMode;
            prefs.TouchpadSensitivity = TouchpadSensitivity;
            prefs.GyroEnabled = GyroEnabled;
            prefs.AccelEnabled = AccelEnabled;
            prefs.RightTriggerMode = RightTriggerMode;
            prefs.LeftTriggerMode = LeftTriggerMode;

            prefs.ButtonRemap.Clear();
            foreach (var row in RemapRows)
            {
                if (row.SelectedTarget != VirtualPadBridge.DefaultButtonMap[row.Source])
                {
                    prefs.ButtonRemap[row.Source.ToString()] = row.SelectedTarget.ToString();
                }
            }
        });

        Close();
    }
}

public enum ConfigCategory
{
    Remap,
    Led,
    Rumble,
    GyroAccel,
    Touchpad,
    AdaptiveTriggers,
}

public sealed class ConfigCategoryDisplayNameConverter : System.Windows.Data.IValueConverter
{
    private static readonly Dictionary<ConfigCategory, string> Names = new()
    {
        [ConfigCategory.Remap] = "Button Remap",
        [ConfigCategory.Led] = "LED Color",
        [ConfigCategory.Rumble] = "Rumble",
        [ConfigCategory.GyroAccel] = "Gyro / Accelerometer",
        [ConfigCategory.Touchpad] = "Touchpad",
        [ConfigCategory.AdaptiveTriggers] = "Adaptive Triggers",
    };

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is ConfigCategory category ? Names[category] : value ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RemapRow
{
    public PadButtons Source { get; }
    public string SourceName => Source.ToString();
    public List<Xbox360Button> TargetOptions { get; } = GridShell.Core.Controllers.Xbox360ButtonCatalog.All.ToList();
    public Xbox360Button SelectedTarget { get; set; }

    public RemapRow(PadButtons source, Xbox360Button defaultTarget)
    {
        Source = source;
        SelectedTarget = defaultTarget;
    }
}
