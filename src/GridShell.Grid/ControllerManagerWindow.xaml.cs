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

/// <summary>Lists currently-connected controllers (via PadReader's per-device enumeration) and,
/// per device, lets the user spin up a virtual Xbox360 pad (VirtualPadBridge/ViGEm) fed from it
/// and optionally hide the device's own raw HID interface(s) (HidHideBridge) so other apps only
/// see the virtual pad, not both. Entirely separate from PadReader.Read()'s own merged-reading
/// navigation path - this reads one specific device at a time via PadReader.ReadDevice.</summary>
public partial class ControllerManagerWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _feedTimer = new() { Interval = TimeSpan.FromMilliseconds(8) };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ObservableCollection<ControllerEntry> Devices { get; } = new();

    public Visibility NoDevicesVisibility => Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public ControllerManagerWindow()
    {
        InitializeComponent();
        DataContext = this;
        this.HideWindowIcon();

        // Both processes read controllers via PadReader - whitelisting both up front means
        // hiding a device's raw HID interface(s) never blocks GridShell's own feed loop,
        // regardless of which process happens to own the active virtual pad for it.
        if (HidHideBridge.IsInstalled())
        {
            TryRegisterSelfAndSibling();
        }

        _refreshTimer.Tick += (_, _) => RefreshDevices();
        _refreshTimer.Start();
        RefreshDevices();

        _feedTimer.Tick += (_, _) => FeedActiveVirtualPads();
        _feedTimer.Start();

        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _feedTimer.Stop();
            foreach (var device in Devices)
            {
                device.StopVirtualPad();
            }
        };
    }

    private static void TryRegisterSelfAndSibling()
    {
        try
        {
            HidHideBridge.RegisterApplication(Environment.ProcessPath!);
            HidHideBridge.RegisterApplication(SiblingExePath.Resolve("GridShell", "GridShell.exe"));
        }
        catch
        {
            // Best-effort - hiding stays unavailable (CanToggleHide false) if this fails,
            // rather than blocking the window from opening at all.
        }
    }

    private void RefreshDevices()
    {
        var current = PadReader.ListDevices();
        var currentIds = current.Select(d => d.Id).ToHashSet();

        foreach (var stale in Devices.Where(d => !currentIds.Contains(d.Id)).ToList())
        {
            stale.StopVirtualPad();
            Devices.Remove(stale);
        }

        foreach (var device in current)
        {
            if (Devices.All(d => d.Id != device.Id))
            {
                Devices.Add(new ControllerEntry(device));
            }
        }

        OnPropertyChanged(nameof(NoDevicesVisibility));
    }

    private void FeedActiveVirtualPads()
    {
        foreach (var device in Devices)
        {
            device.FeedIfActive();
        }
    }

    private void VirtualPadButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ControllerEntry entry)
        {
            return;
        }

        if (entry.HasVirtualPad)
        {
            entry.StopVirtualPad();
            return;
        }

        try
        {
            entry.StartVirtualPad();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't create a virtual controller for {entry.Name}:\n\n{ex.Message}\n\nMake sure the ViGEmBus driver is installed.",
                "Controller Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ControllerEntry entry)
        {
            return;
        }

        new ControllerConfigWindow(entry.ToPadDevice()) { Owner = this }.ShowDialog();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ControllerEntry entry)
        {
            return;
        }

        try
        {
            if (entry.IsHidden)
            {
                entry.UnhideOriginal();
            }
            else
            {
                entry.HideOriginal();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"HidHide operation failed:\n\n{ex.Message}", "Controller Manager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

/// <summary>One row in Controller Manager's list - wraps a PadDevice with the mutable
/// virtual-pad/hide state the UI binds to.</summary>
public sealed class ControllerEntry : INotifyPropertyChanged
{
    private VirtualPadBridge? _virtualPad;
    private List<string> _hiddenInstancePaths = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public uint Id { get; }
    public string Name { get; }
    public ushort VendorId { get; }
    public ushort ProductId { get; }

    public ControllerEntry(PadDevice device)
    {
        Id = device.Id;
        Name = device.Name;
        VendorId = device.VendorId;
        ProductId = device.ProductId;
    }

    public PadDevice ToPadDevice() => new(Id, Name, VendorId, ProductId);

    public bool HasTouchpad => PadReader.TouchpadCount(Id) > 0;
    public List<TouchpadClickMode> TouchpadClickModeOptions { get; } = Enum.GetValues<TouchpadClickMode>().ToList();

    /// <summary>Quick-access toggle, right in Controller Manager's own list - writes straight
    /// to ControllerPreferencesStore the moment it's changed, unlike ControllerConfigWindow's
    /// copy of the same setting (which only saves on that window's own Save button).</summary>
    public TouchpadClickMode TouchpadClickMode
    {
        get
        {
            var prefs = ControllerPreferencesStore.Load().GetValueOrDefault(ControllerPreferencesStore.KeyFor(VendorId, ProductId));
            return prefs?.TouchpadClickMode ?? TouchpadClickMode.Off;
        }
        set
        {
            var allPrefs = ControllerPreferencesStore.Load();
            var key = ControllerPreferencesStore.KeyFor(VendorId, ProductId);
            var prefs = allPrefs.GetValueOrDefault(key) ?? new ControllerPreferences();
            prefs.TouchpadClickMode = value;
            allPrefs[key] = prefs;
            ControllerPreferencesStore.Save(allPrefs);
            OnPropertyChanged(nameof(TouchpadClickMode));
        }
    }

    public bool HasVirtualPad => _virtualPad is not null;
    public bool IsHidden => _hiddenInstancePaths.Count > 0;
    public bool CanToggleHide => HasVirtualPad && HidHideBridge.IsInstalled();

    public string VirtualPadButtonText => HasVirtualPad ? "Remove Virtual Pad" : "Create Virtual Pad";
    public string HideButtonText => IsHidden ? "Unhide Original" : "Hide Original";

    public string StatusText =>
        $"VID 0x{VendorId:X4} / PID 0x{ProductId:X4}" +
        (HasVirtualPad ? " — virtual pad active" : "") +
        (IsHidden ? ", original hidden" : "");

    public void StartVirtualPad()
    {
        var prefs = ControllerPreferencesStore.Load()
            .GetValueOrDefault(ControllerPreferencesStore.KeyFor(VendorId, ProductId));

        var remap = new Dictionary<PadButtons, Xbox360Button>();
        if (prefs is not null)
        {
            foreach (var (sourceName, targetName) in prefs.ButtonRemap)
            {
                if (Enum.TryParse<PadButtons>(sourceName, out var source) &&
                    GridShell.Core.Controllers.Xbox360ButtonCatalog.TryParse(targetName, out var target))
                {
                    remap[source] = target;
                }
            }
        }

        _virtualPad = new VirtualPadBridge(remap);

        if (prefs is { LedRed: { } r, LedGreen: { } g, LedBlue: { } b })
        {
            PadReader.SetLed(Id, r, g, b);
        }

        if (prefs?.GyroEnabled == true)
        {
            PadReader.SetSensorEnabled(Id, PadSensorType.Gyro, true);
        }

        if (prefs?.AccelEnabled == true)
        {
            PadReader.SetSensorEnabled(Id, PadSensorType.Accel, true);
        }

        RaiseAll();
    }

    public void StopVirtualPad()
    {
        if (IsHidden)
        {
            UnhideOriginal();
        }

        _virtualPad?.Dispose();
        _virtualPad = null;
        RaiseAll();
    }

    public void FeedIfActive()
    {
        if (_virtualPad is null)
        {
            return;
        }

        if (PadReader.ReadDevice(Id) is { } reading)
        {
            _virtualPad.Feed(reading);
        }
    }

    public void HideOriginal()
    {
        var matches = HidHideBridge.ListGamingDevicesParsed()
            .Where(d => d.VendorId == VendorId && d.ProductId == ProductId)
            .ToList();

        foreach (var match in matches)
        {
            HidHideBridge.HideDevice(match.DeviceInstancePath);
        }

        HidHideBridge.SetCloakEnabled(true);
        _hiddenInstancePaths = matches.Select(m => m.DeviceInstancePath).ToList();
        RaiseAll();
    }

    public void UnhideOriginal()
    {
        foreach (var path in _hiddenInstancePaths)
        {
            HidHideBridge.UnhideDevice(path);
        }

        _hiddenInstancePaths.Clear();
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(HasVirtualPad));
        OnPropertyChanged(nameof(IsHidden));
        OnPropertyChanged(nameof(CanToggleHide));
        OnPropertyChanged(nameof(VirtualPadButtonText));
        OnPropertyChanged(nameof(HideButtonText));
        OnPropertyChanged(nameof(StatusText));
    }
}
