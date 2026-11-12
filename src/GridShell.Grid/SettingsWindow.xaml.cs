using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GridShell.Core;
using GridShell.Core.GameLibraries;
using GridShell.Core.Interop;
using GridShell.Core.Interop.Audio;
using GridShell.Core.Interop.Bluetooth;
using GridShell.Core.Interop.Display;
using GridShell.Core.Interop.WiFi;
using GridShell.Core.Tiles;
using GridShell.Core.Updates;
using GridShell.Core.Widgets;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;
using AppSettings = GridShell.Core.Settings.AppSettings;
using AppTheme = GridShell.Core.Settings.AppTheme;
using TileSizeOption = GridShell.Core.Settings.TileSize;
using ThemePalette = GridShell.Core.Settings.ThemePalette;
using UiFontChoice = GridShell.Core.Settings.UiFontChoice;
using EDataFlow = Vanara.PInvoke.CoreAudio.EDataFlow;

namespace GridShell;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private readonly List<Tile> _allTiles;
    private readonly AppSettings _settings;
    private readonly Action _onTilesChanged;
    private readonly Action _onSettingsChanged;

    private bool _autostartEnabled;
    private DetectedBrowser? _selectedDefaultBrowser;
    private bool _winKeyHookEnabled;
    private TileSizeOption _tileSize;
    private AppTheme _theme;
    private UiFontChoice _uiFont;
    private string? _startButtonIconImagePath;
    private bool _useGridShellAsSystemShell;
    private string _igdbClientId = "";
    private string _wallpaperUrl = "";
    private bool _wallpaperMuted;

    private bool _isCheckingForUpdate;
    private bool _isUpdateAvailable;
    private bool _isDownloadingUpdate;
    private double _updateDownloadProgress;
    private string _updateStatusText = $"Version {UpdateChecker.CurrentVersion}";
    private string? _latestReleaseUrl;

    private static readonly string WatchdogExePath = SiblingExePath.Resolve("GridShell.Watchdog", "GridShell.Watchdog.exe");

    public ObservableCollection<DetectedBrowser> Browsers { get; } = new();
    public ObservableCollection<Tile> AppTiles { get; } = new();
    public ObservableCollection<Tile> WebTiles { get; } = new();
    public ObservableCollection<Tile> GameTiles { get; } = new();

    private List<DesktopWidget> _allWidgets = new();
    public ObservableCollection<DesktopWidget> Widgets { get; } = new();

    private static readonly string[] PresetAccentColors =
        { "#4A90D9", "#D9534F", "#5CB85C", "#F0AD4E", "#9B59B6", "#1ABC9C" };

    public ObservableCollection<AccentSwatchItem> AccentSwatches { get; } = new();

    private static readonly (string Codepoint, string Name)[] StartIconChoices =
    {
        ("e5c3", "Apps"),
        ("e9b0", "Grid"),
        ("e871", "Dashboard"),
        ("e1bd", "Widgets"),
    };

    public ObservableCollection<StartIconOption> StartIconOptions { get; } = new();

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();

    private AudioDeviceInfo? _selectedOutputDevice;
    private AudioDeviceInfo? _selectedInputDevice;
    private double _outputVolume;
    private double _inputVolume;
    private bool _outputMuted;
    private bool _inputMuted;

    // Guards against the SelectedOutputDevice/SelectedInputDevice setters firing
    // SetDefaultDevice while RefreshAudioDevices is just populating the lists for the first
    // time (setting SelectedItem on a ComboBox binding raises the property setter same as a
    // real user pick would).
    private bool _loadingAudioDevices;

    public AudioDeviceInfo? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set
        {
            _selectedOutputDevice = value;
            OnPropertyChanged();
            if (!_loadingAudioDevices && value is not null)
            {
                AudioDeviceManager.SetDefaultDevice(value.Id);
            }
        }
    }

    public AudioDeviceInfo? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set
        {
            _selectedInputDevice = value;
            OnPropertyChanged();
            if (!_loadingAudioDevices && value is not null)
            {
                AudioDeviceManager.SetDefaultDevice(value.Id);
            }
        }
    }

    public double OutputVolume
    {
        get => _outputVolume;
        set
        {
            _outputVolume = value;
            OnPropertyChanged();
            AudioDeviceManager.SetVolume(EDataFlow.eRender, (float)(value / 100.0));
        }
    }

    public double InputVolume
    {
        get => _inputVolume;
        set
        {
            _inputVolume = value;
            OnPropertyChanged();
            AudioDeviceManager.SetVolume(EDataFlow.eCapture, (float)(value / 100.0));
        }
    }

    public bool OutputMuted
    {
        get => _outputMuted;
        set
        {
            _outputMuted = value;
            OnPropertyChanged();
            AudioDeviceManager.SetMute(EDataFlow.eRender, value);
        }
    }

    public bool InputMuted
    {
        get => _inputMuted;
        set
        {
            _inputMuted = value;
            OnPropertyChanged();
            AudioDeviceManager.SetMute(EDataFlow.eCapture, value);
        }
    }

    private void RefreshAudioDevices()
    {
        _loadingAudioDevices = true;
        try
        {
            OutputDevices.Clear();
            foreach (var device in AudioDeviceManager.EnumerateDevices(EDataFlow.eRender))
            {
                OutputDevices.Add(device);
            }

            InputDevices.Clear();
            foreach (var device in AudioDeviceManager.EnumerateDevices(EDataFlow.eCapture))
            {
                InputDevices.Add(device);
            }

            _selectedOutputDevice = OutputDevices.FirstOrDefault(d => d.IsDefault);
            OnPropertyChanged(nameof(SelectedOutputDevice));
            _selectedInputDevice = InputDevices.FirstOrDefault(d => d.IsDefault);
            OnPropertyChanged(nameof(SelectedInputDevice));
        }
        finally
        {
            _loadingAudioDevices = false;
        }

        _outputVolume = AudioDeviceManager.GetVolume(EDataFlow.eRender) * 100.0;
        OnPropertyChanged(nameof(OutputVolume));
        _inputVolume = AudioDeviceManager.GetVolume(EDataFlow.eCapture) * 100.0;
        OnPropertyChanged(nameof(InputVolume));
        _outputMuted = AudioDeviceManager.GetMute(EDataFlow.eRender);
        OnPropertyChanged(nameof(OutputMuted));
        _inputMuted = AudioDeviceManager.GetMute(EDataFlow.eCapture);
        OnPropertyChanged(nameof(InputMuted));
    }

    public ObservableCollection<PairedBluetoothDevice> PairedBluetoothDevices { get; } = new();
    public ObservableCollection<DiscoveredBluetoothDevice> DiscoveredBluetoothDevices { get; } = new();

    private bool _bluetoothRadioOn;
    private bool _isScanningBluetooth;
    private System.Timers.Timer? _bluetoothWatcher;

    public bool BluetoothRadioOn
    {
        get => _bluetoothRadioOn;
        set
        {
            _bluetoothRadioOn = value;
            OnPropertyChanged();
            _ = BluetoothRadioManager.SetOnAsync(value);
        }
    }

    public bool IsScanningBluetooth
    {
        get => _isScanningBluetooth;
        set { _isScanningBluetooth = value; OnPropertyChanged(); }
    }

    private async Task RefreshBluetoothAsync()
    {
        _bluetoothRadioOn = await BluetoothRadioManager.IsOnAsync();
        OnPropertyChanged(nameof(BluetoothRadioOn));

        PairedBluetoothDevices.Clear();
        foreach (var device in await BluetoothDeviceManager.EnumeratePairedDevicesAsync())
        {
            PairedBluetoothDevices.Add(device);
        }
    }

    private void ScanBluetoothButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsScanningBluetooth)
        {
            if (_bluetoothWatcher is not null)
            {
                BluetoothDeviceManager.StopDiscovery(_bluetoothWatcher);
                _bluetoothWatcher = null;
            }

            IsScanningBluetooth = false;
            return;
        }

        DiscoveredBluetoothDevices.Clear();
        IsScanningBluetooth = true;
        _bluetoothWatcher = BluetoothDeviceManager.StartDiscovery(device =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!DiscoveredBluetoothDevices.Any(d => d.Id == device.Id))
                {
                    DiscoveredBluetoothDevices.Add(device);
                }
            });
        });
    }

    private async void PairBluetoothButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not DiscoveredBluetoothDevice device)
        {
            return;
        }

        // Windows shows its own native pairing wizard dialog here (BluetoothAuthenticateDevice
        // with a null passkey) - no GridShell-owned dialog needed.
        var paired = await BluetoothDeviceManager.PairAsync(device.Id);

        if (paired)
        {
            DiscoveredBluetoothDevices.Remove(device);
            await RefreshBluetoothAsync();
        }
        else
        {
            System.Windows.MessageBox.Show(
                this, $"Couldn't pair with {device.Name}.", "Pairing failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void UnpairBluetoothButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not PairedBluetoothDevice device)
        {
            return;
        }

        await BluetoothDeviceManager.UnpairAsync(device.Id);
        await RefreshBluetoothAsync();
    }

    public ObservableCollection<WiFiNetworkInfo> WiFiNetworks { get; } = new();
    public ObservableCollection<string> SavedWiFiNetworks { get; } = new();

    private bool _wifiRadioOn;
    private bool _isScanningWiFi;

    public bool WiFiRadioOn
    {
        get => _wifiRadioOn;
        set
        {
            _wifiRadioOn = value;
            OnPropertyChanged();
            _ = WiFiRadioManager.SetOnAsync(value);
        }
    }

    public bool IsScanningWiFi
    {
        get => _isScanningWiFi;
        set { _isScanningWiFi = value; OnPropertyChanged(); }
    }

    private async Task RefreshWiFiRadioAsync()
    {
        _wifiRadioOn = await WiFiRadioManager.IsOnAsync();
        OnPropertyChanged(nameof(WiFiRadioOn));
    }

    private async Task RefreshSavedWiFiNetworksAsync()
    {
        var profiles = await WiFiNetworkManager.ListSavedProfilesAsync();
        SavedWiFiNetworks.Clear();
        foreach (var profile in profiles)
        {
            SavedWiFiNetworks.Add(profile);
        }
    }

    private async void ConnectSavedWiFiButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string profileName)
        {
            return;
        }

        var result = await WiFiNetworkManager.ConnectToSavedAsync(profileName);
        if (result != WiFiConnectResult.Success)
        {
            System.Windows.MessageBox.Show(
                this, $"Couldn't connect to {profileName}.", "Connection failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RefreshWiFiNetworksAsync();
    }

    private async void ForgetWiFiButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string profileName)
        {
            return;
        }

        await WiFiNetworkManager.ForgetProfileAsync(profileName);
        await RefreshSavedWiFiNetworksAsync();
    }

    private async void ScanWiFiButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshWiFiNetworksAsync();
        await RefreshSavedWiFiNetworksAsync();
    }

    private async Task RefreshWiFiNetworksAsync()
    {
        IsScanningWiFi = true;
        try
        {
            var networks = await WiFiNetworkManager.ScanAsync();
            WiFiNetworks.Clear();
            foreach (var network in networks)
            {
                WiFiNetworks.Add(network);
            }
        }
        finally
        {
            IsScanningWiFi = false;
        }
    }

    private async void ConnectWiFiButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not WiFiNetworkInfo network)
        {
            return;
        }

        var result = await WiFiNetworkManager.ConnectAsync(network.Ssid);

        if (result == WiFiConnectResult.NeedsPassword)
        {
            var dialog = new WiFiPasswordWindow(network.Ssid) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.ResultPassword is null)
            {
                return;
            }

            result = await WiFiNetworkManager.ConnectAsync(network.Ssid, dialog.ResultPassword);
        }

        if (result != WiFiConnectResult.Success)
        {
            System.Windows.MessageBox.Show(
                this, $"Couldn't connect to {network.Ssid}.", "Connection failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RefreshWiFiNetworksAsync();
        await RefreshSavedWiFiNetworksAsync();
    }

    public ObservableCollection<MonitorInfo> Monitors { get; } = new();
    public ObservableCollection<ResolutionOption> AvailableResolutions { get; } = new();

    private MonitorInfo? _selectedMonitor;
    private ResolutionOption? _selectedResolution;
    private double _brightnessValue;

    public MonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            _selectedMonitor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSetPrimary));
            OnPropertyChanged(nameof(BrightnessSupported));

            AvailableResolutions.Clear();
            if (value is null)
            {
                return;
            }

            foreach (var resolution in DisplayManager.GetSupportedResolutions(value.DeviceName))
            {
                AvailableResolutions.Add(resolution);
            }

            _selectedResolution = AvailableResolutions.FirstOrDefault(
                r => r.Width == value.CurrentWidth && r.Height == value.CurrentHeight);
            OnPropertyChanged(nameof(SelectedResolution));

            _brightnessValue = value.Brightness;
            OnPropertyChanged(nameof(BrightnessValue));
        }
    }

    public ResolutionOption? SelectedResolution
    {
        get => _selectedResolution;
        set { _selectedResolution = value; OnPropertyChanged(); }
    }

    public bool CanSetPrimary => SelectedMonitor is { IsPrimary: false };
    public bool BrightnessSupported => SelectedMonitor?.BrightnessSupported ?? false;

    public double BrightnessValue
    {
        get => _brightnessValue;
        set
        {
            _brightnessValue = value;
            OnPropertyChanged();
            if (SelectedMonitor is not null)
            {
                DisplayManager.SetBrightness(SelectedMonitor.DeviceName, (uint)value);
            }
        }
    }

    private void RefreshMonitors()
    {
        var previouslySelected = SelectedMonitor?.DeviceName;

        Monitors.Clear();
        foreach (var monitor in DisplayManager.GetMonitors())
        {
            Monitors.Add(monitor);
        }

        SelectedMonitor = Monitors.FirstOrDefault(m => m.DeviceName == previouslySelected) ?? Monitors.FirstOrDefault();
    }

    private void ApplyResolutionButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMonitor is null || SelectedResolution is null)
        {
            return;
        }

        DisplayManager.SetResolution(SelectedMonitor.DeviceName, SelectedResolution);
        RefreshMonitors();
    }

    private void SetPrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMonitor is null)
        {
            return;
        }

        DisplayManager.SetPrimary(SelectedMonitor.DeviceName);
        RefreshMonitors();
    }

    public ObservableCollection<EmulatorPlatformRow> EmulatorPlatforms { get; } = new();
    public ObservableCollection<string> ExtraRomPaths { get; } = new();

    private void LoadEmulatorPlatforms()
    {
        ExtraRomPaths.Clear();
        foreach (var path in _settings.ExtraRomPaths)
        {
            ExtraRomPaths.Add(path);
        }

        EmulatorPlatforms.Clear();
        foreach (var platform in EmulationPlatforms.All)
        {
            var row = new EmulatorPlatformRow
            {
                Platform = platform,
                OnChanged = () => SaveEmulatorConfig(platform.Slug),
            };

            // Must be in the collection before setting EmulatorPath/ArgumentTemplate below —
            // both setters synchronously call OnChanged -> SaveEmulatorConfig, which looks the
            // row up in EmulatorPlatforms by slug.
            EmulatorPlatforms.Add(row);

            if (_settings.EmulatorConfigs.TryGetValue(platform.Slug, out var config))
            {
                row.SuppressOnChanged = true;
                row.EmulatorPath = config.EmulatorPath ?? "";
                row.ArgumentTemplate = config.ArgumentTemplate;
                row.SuppressOnChanged = false;
            }

            row.RefreshRomCount(ExtraRomPaths);
        }
    }

    private void RefreshAllRomCounts()
    {
        foreach (var row in EmulatorPlatforms)
        {
            row.RefreshRomCount(ExtraRomPaths);
        }
    }

    private void AddRomPoolButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose an additional ROM folder",
            InitialDirectory = System.IO.Directory.Exists(EmulationPlatforms.RomsRootPath)
                ? EmulationPlatforms.RomsRootPath
                : "",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = dialog.FolderName;
        if (ExtraRomPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
            || string.Equals(path, EmulationPlatforms.RomsRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ExtraRomPaths.Add(path);
        _settings.ExtraRomPaths = ExtraRomPaths.ToList();
        _onSettingsChanged();
        RefreshAllRomCounts();
    }

    private void RemoveRomPoolButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string path)
        {
            return;
        }

        ExtraRomPaths.Remove(path);
        _settings.ExtraRomPaths = ExtraRomPaths.ToList();
        _onSettingsChanged();
        RefreshAllRomCounts();
    }

    private void SaveEmulatorConfig(string slug)
    {
        var row = EmulatorPlatforms.First(r => r.Platform.Slug == slug);
        _settings.EmulatorConfigs[slug] = new EmulatorConfig
        {
            EmulatorPath = string.IsNullOrWhiteSpace(row.EmulatorPath) ? null : row.EmulatorPath,
            ArgumentTemplate = row.ArgumentTemplate,
        };
        _onSettingsChanged();

        // Newly-pointed-at RetroArch/Dolphin/PCSX2 installs should pick up an already-logged-in
        // RetroAchievements account without the user having to re-trigger the login.
        RetroAchievementsConfigWriter.ApplyToAllConfiguredEmulators(_settings);
    }

    private void RefreshCoresButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is EmulatorPlatformRow row)
        {
            row.RefreshCoreList();
        }
    }

    private void DownloadEmulatorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void DownloadLinkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not EmulatorDownloadLink link)
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.Url) { UseShellExecute = true });
    }

    private void BrowseEmulatorPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not EmulatorPlatformRow row)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            Title = $"Choose the emulator for {row.DisplayName}",
            InitialDirectory = System.IO.Directory.Exists(@"C:\Emulators") ? @"C:\Emulators" : "",
        };

        if (dialog.ShowDialog(this) == true)
        {
            row.EmulatorPath = dialog.FileName;
        }
    }

    private void OpenEmulatorButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not EmulatorPlatformRow { IsConfigured: true } row)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.EmulatorPath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.FileNotFoundException)
        {
            System.Windows.MessageBox.Show(
                this, $"Couldn't open {row.DisplayName}'s emulator.", "Launch failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public bool AutostartEnabled
    {
        get => _autostartEnabled;
        set
        {
            _autostartEnabled = value;
            OnPropertyChanged();

            if (value)
            {
                AutostartManager.Enable(Environment.ProcessPath!);
            }
            else
            {
                AutostartManager.Disable();
            }
        }
    }

    public DetectedBrowser? SelectedDefaultBrowser
    {
        get => _selectedDefaultBrowser;
        set
        {
            _selectedDefaultBrowser = value;
            OnPropertyChanged();
            _settings.DefaultBrowserExecutablePath = value?.ExecutablePath;
            _onSettingsChanged();
        }
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        set { _updateStatusText = value; OnPropertyChanged(); }
    }

    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        set { _isCheckingForUpdate = value; OnPropertyChanged(); }
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set { _isUpdateAvailable = value; OnPropertyChanged(); }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set { _isDownloadingUpdate = value; OnPropertyChanged(); }
    }

    public double UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        set { _updateDownloadProgress = value; OnPropertyChanged(); }
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        IsCheckingForUpdate = true;
        IsUpdateAvailable = false;
        UpdateStatusText = "Checking for updates...";

        try
        {
            var result = await UpdateChecker.CheckAsync();
            _latestReleaseUrl = result.ReleaseUrl;

            if (result.UpdateAvailable)
            {
                IsUpdateAvailable = true;
                UpdateStatusText = $"Update available: {result.LatestVersion} (you have {result.CurrentVersion})";
            }
            else
            {
                UpdateStatusText = $"Version {result.CurrentVersion} - up to date";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            UpdateStatusText = $"Version {UpdateChecker.CurrentVersion} - couldn't check for updates (no internet connection?)";
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    private void ViewReleaseNotesButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _latestReleaseUrl ?? "https://github.com/theskysanctum/gridshell/releases/latest";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateStatusText = "Downloading update...";

        try
        {
            var installerPath = Path.Combine(Path.GetTempPath(), "GridShell-Setup.exe");
            var progress = new Progress<double>(percent =>
            {
                UpdateDownloadProgress = percent;
                UpdateStatusText = $"Downloading update... {percent:F0}%";
            });

            await UpdateChecker.DownloadInstallerAsync(installerPath, progress);

            UpdateStatusText = "Starting the installer...";
            LaunchInstallerAndExit(installerPath);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            UpdateStatusText = "Download failed - check your internet connection and try again.";
            IsDownloadingUpdate = false;
        }
    }

    /// <summary>Launches the just-downloaded installer, then tears down every GridShell process
    /// so none of them still hold their own exe files open when Inno Setup tries to overwrite
    /// them - the installer's own [Run] step relaunches GridShell once it's done.</summary>
    private void LaunchInstallerAndExit(string installerPath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true });

        // Tell the taskbar process to exit with ShellExitCodes.Updating (not the usual
        // RestoreExplorer) before closing it, so GridShell.Watchdog - if this is running as the
        // Windows shell - knows not to relaunch it mid-install.
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("GridShell"))
        {
            foreach (var hwnd in ProcessWindowCloser.FindWindowsOf(process.Id))
            {
                PostMessage(hwnd, PrepareForUpdateMessage.Id, IntPtr.Zero, IntPtr.Zero);
            }

            ProcessWindowCloser.CloseAnyWindowOf(process.Id);
        }

        // This process (GridShell.Grid, hosting this very Settings window) also needs to let go
        // of its own exe/dll files - a normal clean exit, no special exit code needed since
        // nothing watches this process the way Watchdog watches GridShell.exe.
        Application.Current.Shutdown();
    }

    public bool WinKeyHookEnabled
    {
        get => _winKeyHookEnabled;
        set
        {
            _winKeyHookEnabled = value;
            OnPropertyChanged();
            _settings.WinKeyHookEnabled = value;
            _onSettingsChanged();
        }
    }

    public string IgdbClientId
    {
        get => _igdbClientId;
        set
        {
            _igdbClientId = value;
            OnPropertyChanged();
            _settings.IgdbClientId = value;
            _onSettingsChanged();
        }
    }

    private void IgdbClientSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _settings.IgdbClientSecret = IgdbClientSecretBox.Password;
        _onSettingsChanged();
    }

    private void RealDebridApiTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _settings.RealDebridApiToken = RealDebridApiTokenBox.Password;
        _onSettingsChanged();
    }

    public string RetroAchievementsUsernameInput { get; set; } = "";

    public bool RetroAchievementsLoggedIn =>
        !string.IsNullOrWhiteSpace(_settings.RetroAchievementsUsername) && !string.IsNullOrWhiteSpace(_settings.RetroAchievementsToken);

    public string? RetroAchievementsUsername => _settings.RetroAchievementsUsername;

    private string _retroAchievementsStatus = "";
    public string RetroAchievementsStatus
    {
        get => _retroAchievementsStatus;
        set
        {
            _retroAchievementsStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RetroAchievementsStatusVisible));
        }
    }

    public bool RetroAchievementsStatusVisible => !string.IsNullOrWhiteSpace(RetroAchievementsStatus);

    private async void RetroAchievementsLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var username = RaUsernameBox.Text.Trim();
        var password = RaPasswordBox.Password;
        if (username.Length == 0 || password.Length == 0)
        {
            RetroAchievementsStatus = "Enter a username and password.";
            return;
        }

        RetroAchievementsStatus = "Logging in...";
        var result = await GridShell.Core.RetroAchievements.RetroAchievementsClient.LoginAsync(username, password);
        if (!result.Success)
        {
            RetroAchievementsStatus = "Login failed - check your username/password.";
            return;
        }

        _settings.RetroAchievementsUsername = result.Username;
        _settings.RetroAchievementsToken = result.Token;
        RaPasswordBox.Password = "";
        RetroAchievementsStatus = "";
        OnPropertyChanged(nameof(RetroAchievementsLoggedIn));
        OnPropertyChanged(nameof(RetroAchievementsUsername));

        _onSettingsChanged();
        RetroAchievementsConfigWriter.ApplyToAllConfiguredEmulators(_settings);
    }

    private void RetroAchievementsLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.RetroAchievementsUsername = null;
        _settings.RetroAchievementsToken = null;
        OnPropertyChanged(nameof(RetroAchievementsLoggedIn));
        OnPropertyChanged(nameof(RetroAchievementsUsername));
        _onSettingsChanged();
    }

    public string? DownloadManagerFolder
    {
        get => _settings.DownloadManagerFolder;
        set
        {
            _settings.DownloadManagerFolder = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
            _onSettingsChanged();
        }
    }

    private void BrowseDownloadFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a download folder" };
        if (dialog.ShowDialog(this) == true)
        {
            DownloadManagerFolder = dialog.FolderName;
        }
    }

    private void OpenExtensionFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "ChromeExtension");
        if (System.IO.Directory.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    public TileSizeOption TileSize
    {
        get => _tileSize;
        set
        {
            _tileSize = value;
            OnPropertyChanged();
            _settings.TileSize = value;
            _onSettingsChanged();
        }
    }

    /// <summary>A local .html file path or http(s) URL to render as the desktop "wallpaper"
    /// instead of the default (the actual current Windows wallpaper image) — rendered by
    /// WallpaperWindow inside the taskbar process, not this one, hence the explicit
    /// NotifyTaskbarSettingsChanged below rather than just ApplySettings locally.</summary>
    public string WallpaperUrl
    {
        get => _wallpaperUrl;
        set
        {
            _wallpaperUrl = value;
            OnPropertyChanged();
            _settings.WallpaperUrl = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged(nameof(WidgetsAvailable));
            OnPropertyChanged(nameof(CanEditWidgets));
            _onSettingsChanged();
            NotifyTaskbarSettingsChanged();
        }
    }

    /// <summary>Whether the wallpaper is set to a video/image file at all - the default
    /// wallpaper (no WallpaperUrl override, falling back to the real current Windows wallpaper
    /// image) deliberately doesn't count, nor does an .html page or a bare remote URL without
    /// a recognized media extension. ANDed with Kiosk Mode via CanEditWidgets below, not
    /// replaced by it - the two gates are independent reasons the tab can be unusable.</summary>
    public bool WidgetsAvailable =>
        !string.IsNullOrWhiteSpace(_settings.WallpaperUrl)
        && (MediaWrapperHtml.IsVideo(_settings.WallpaperUrl) || MediaWrapperHtml.IsImage(_settings.WallpaperUrl));

    public bool CanEditTiles => !KioskModeEnabled;
    public bool CanEditAppearance => !KioskModeEnabled;
    public bool CanEditWidgets => !KioskModeEnabled && WidgetsAvailable;

    public string KioskModeButtonText => KioskModeEnabled ? "Disable Kiosk Mode..." : "Enable Kiosk Mode...";

    /// <summary>Locks down Tiles/Appearance/Widgets editing and exiting GridShell. Changing
    /// this directly (private setter) is only ever done after KioskModeButton_Click's PIN flow
    /// succeeds - there's no plain two-way-bound checkbox, since turning it on requires setting
    /// a PIN first and turning it off requires verifying one.</summary>
    public bool KioskModeEnabled
    {
        get => _settings.KioskModeEnabled;
        private set
        {
            _settings.KioskModeEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KioskModeButtonText));
            OnPropertyChanged(nameof(CanEditTiles));
            OnPropertyChanged(nameof(CanEditAppearance));
            OnPropertyChanged(nameof(CanEditWidgets));
            _onSettingsChanged();
            NotifyTaskbarSettingsChanged();
        }
    }

    private void KioskModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (KioskModeEnabled)
        {
            var dialog = new KioskPinWindow(_settings.KioskModePinHash!, "Enter your PIN to disable Kiosk Mode.") { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                KioskModeEnabled = false;
                _settings.KioskModePinHash = null;
            }
        }
        else
        {
            var dialog = new KioskPinWindow { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ResultPinHash is { } hash)
            {
                _settings.KioskModePinHash = hash;
                KioskModeEnabled = true;
            }
        }
    }

    public bool WallpaperMuted
    {
        get => _wallpaperMuted;
        set
        {
            _wallpaperMuted = value;
            OnPropertyChanged();
            _settings.WallpaperMuted = value;
            _onSettingsChanged();
            NotifyTaskbarSettingsChanged();
        }
    }

    private void BrowseWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a wallpaper image, video, or HTML page",
            Filter = "Wallpaper files (*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.mp4;*.webm;*.mov;*.mkv;*.avi;*.html;*.htm)|" +
                     "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.mp4;*.webm;*.mov;*.mkv;*.avi;*.html;*.htm|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            WallpaperUrl = dialog.FileName;
        }
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            OnPropertyChanged();
            _settings.Theme = value;

            // Applies to this process immediately (Application.Resources covers this window,
            // MainWindow, AddTileWindow and RenameTileWindow all at once) — the taskbar is a
            // separate process with its own copy, notified below instead.
#pragma warning disable WPF0001 // ThemeMode is experimental, same as GridShell.Grid's App.xaml.cs startup
            Application.Current.ThemeMode = value == AppTheme.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
            ThemePalette.Apply(Application.Current.Resources, value);

            _onSettingsChanged();
            NotifyTaskbarSettingsChanged();
        }
    }

    public UiFontChoice UiFont
    {
        get => _uiFont;
        set
        {
            _uiFont = value;
            OnPropertyChanged();
            _settings.UiFont = value;
            ApplyFont();

            _onSettingsChanged();
            NotifyTaskbarSettingsChanged();
        }
    }

    public string? CustomFontPath
    {
        get => _settings.CustomFontPath;
        set
        {
            _settings.CustomFontPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
            if (_uiFont == UiFontChoice.Custom)
            {
                ApplyFont();
            }

            _onSettingsChanged();
            NotifyTaskbarSettingsChanged();
        }
    }

    private void BrowseCustomFontButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a font file",
            Filter = "Font files (*.ttf;*.otf)|*.ttf;*.otf|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (Fonts.GetFontFamilies(new Uri(dialog.FileName)).FirstOrDefault() is null)
        {
            System.Windows.MessageBox.Show(this, "That file doesn't look like a usable font.", "Custom font",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.CustomFontPath = dialog.FileName;
        OnPropertyChanged(nameof(CustomFontPath));
        UiFont = UiFontChoice.Custom;
    }

    // Applies to this process immediately (Application.Resources covers this window,
    // MainWindow, AddTileWindow and RenameTileWindow all at once) - the taskbar is a separate
    // process with its own copy, notified via NotifyTaskbarSettingsChanged at each call site.
    private void ApplyFont() => Application.Current.Resources["AppFontFamily"] = UiFontResolver.Resolve(_settings);

    private void StartIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not StartIconOption option)
        {
            return;
        }

        _settings.StartButtonIconCodepoint = option.Codepoint;
        _settings.StartButtonIconImagePath = null;
        _startButtonIconImagePath = null;
        OnPropertyChanged(nameof(StartButtonIconImagePath));
        RefreshStartIconOptions();

        _onSettingsChanged();
        NotifyTaskbarSettingsChanged();
    }

    public string? StartButtonIconImagePath
    {
        get => _startButtonIconImagePath;
        set
        {
            _startButtonIconImagePath = value;
            OnPropertyChanged();
            _settings.StartButtonIconImagePath = string.IsNullOrWhiteSpace(value) ? null : value;
            RefreshStartIconOptions();

            _onSettingsChanged();
            NotifyTaskbarSettingsChanged();
        }
    }

    private void BrowseStartIconButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a Start button icon image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            StartButtonIconImagePath = dialog.FileName;
        }
    }

    private void ClearStartIconImageButton_Click(object sender, RoutedEventArgs e) => StartButtonIconImagePath = null;

    private void RefreshStartIconOptions()
    {
        StartIconOptions.Clear();

        var customImageActive = !string.IsNullOrWhiteSpace(_settings.StartButtonIconImagePath);
        foreach (var (codepoint, name) in StartIconChoices)
        {
            StartIconOptions.Add(new StartIconOption
            {
                Codepoint = codepoint,
                Name = name,
                IsSelected = !customImageActive &&
                    string.Equals(codepoint, _settings.StartButtonIconCodepoint, StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    public bool UseGridShellAsSystemShell
    {
        get => _useGridShellAsSystemShell;
        set
        {
            if (_useGridShellAsSystemShell == value)
            {
                return;
            }

            if (value)
            {
                var confirmed = ConfirmationWindow.Confirm(
                    this,
                    "Replace Windows Explorer as the shell?",
                    "This replaces Windows Explorer as the shell for EVERY account on this PC " +
                    "(machine-wide, not just yours), and for Safe Mode too. It only takes effect " +
                    "on the next login — this session keeps running normally either way. " +
                    "You'll see a Windows admin (UAC) prompt next to confirm the registry change.\n\n" +
                    "If something goes wrong after that: press Ctrl+Shift+Esc, then File → " +
                    "Run new task → explorer.exe to get back to a normal desktop immediately.");

                if (!confirmed)
                {
                    // Backing field never changed — re-raise so the checkbox's binding re-reads
                    // the unchanged value and visually snaps back to unchecked.
                    OnPropertyChanged();
                    return;
                }

                if (!ShellRegistration.RegisterAsShell(WatchdogExePath))
                {
                    ConfirmationWindow.Info(this, "Not changed",
                        "The admin prompt was declined or the change failed, so nothing was modified.");
                    OnPropertyChanged();
                    return;
                }
            }
            else
            {
                if (!ShellRegistration.UnregisterAsShell())
                {
                    ConfirmationWindow.Info(this, "Not changed",
                        "The admin prompt was declined or the change failed, so nothing was modified.");
                    OnPropertyChanged();
                    return;
                }
            }

            _useGridShellAsSystemShell = value;
            OnPropertyChanged();
        }
    }

    private static void NotifyTaskbarSettingsChanged()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("GridShell"))
        {
            foreach (var hwnd in ProcessWindowCloser.FindWindowsOf(process.Id))
            {
                PostMessage(hwnd, SettingsChangedMessage.Id, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    public SettingsWindow(List<Tile> allTiles, AppSettings settings, Action onTilesChanged, Action onSettingsChanged)
    {
        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.HideWindowIcon();
        this.ScaleToWorkArea(widthFraction: 0.6, heightFraction: 0.92,
            minWidth: 520, minHeight: 560, maxWidth: 760, maxHeight: 940);

        _allTiles = allTiles;
        _settings = settings;
        _onTilesChanged = onTilesChanged;
        _onSettingsChanged = onSettingsChanged;

        Loaded += SettingsWindow_Loaded;
        Closed += (_, _) =>
        {
            if (_bluetoothWatcher is not null)
            {
                BluetoothDeviceManager.StopDiscovery(_bluetoothWatcher);
            }
        };
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var browser in BrowserDetector.DetectInstalled())
        {
            Browsers.Add(browser);
        }

        _selectedDefaultBrowser = Browsers.FirstOrDefault(b => b.ExecutablePath == _settings.DefaultBrowserExecutablePath);
        OnPropertyChanged(nameof(SelectedDefaultBrowser));

        _autostartEnabled = AutostartManager.IsEnabled;
        OnPropertyChanged(nameof(AutostartEnabled));

        _winKeyHookEnabled = _settings.WinKeyHookEnabled;
        OnPropertyChanged(nameof(WinKeyHookEnabled));

        _igdbClientId = _settings.IgdbClientId ?? "";
        OnPropertyChanged(nameof(IgdbClientId));
        IgdbClientSecretBox.Password = _settings.IgdbClientSecret ?? "";

        RealDebridApiTokenBox.Password = _settings.RealDebridApiToken ?? "";
        OnPropertyChanged(nameof(DownloadManagerFolder));

        _wallpaperUrl = _settings.WallpaperUrl ?? "";
        OnPropertyChanged(nameof(WallpaperUrl));

        _wallpaperMuted = _settings.WallpaperMuted;
        OnPropertyChanged(nameof(WallpaperMuted));

        _tileSize = _settings.TileSize;
        OnPropertyChanged(nameof(TileSize));

        _theme = _settings.Theme;
        OnPropertyChanged(nameof(Theme));

        _uiFont = _settings.UiFont;
        OnPropertyChanged(nameof(UiFont));

        _startButtonIconImagePath = _settings.StartButtonIconImagePath;
        OnPropertyChanged(nameof(StartButtonIconImagePath));

        _useGridShellAsSystemShell = ShellRegistration.IsGridShellRegisteredAsShell(WatchdogExePath);
        OnPropertyChanged(nameof(UseGridShellAsSystemShell));

        RefreshAccentSwatches();
        RefreshStartIconOptions();
        RefreshAudioDevices();
        await RefreshBluetoothAsync();
        await RefreshWiFiRadioAsync();
        await RefreshSavedWiFiNetworksAsync();
        RefreshMonitors();
        LoadEmulatorPlatforms();

        // Checked once automatically on open - not a silent background poller, just so opening
        // Settings alone already surfaces an available update instead of needing an extra click.
        CheckForUpdatesButton_Click(this, new RoutedEventArgs());

        RefreshTileList(TileKind.App);
        RefreshTileList(TileKind.Website);
        RefreshTileList(TileKind.Game);

        _allWidgets = WidgetStore.Load();
        RefreshWidgetList();
    }

    private void RefreshWidgetList()
    {
        Widgets.Clear();
        foreach (var widget in _allWidgets)
        {
            Widgets.Add(widget);
        }
    }

    private void RemoveWidgetButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not DesktopWidget widget)
        {
            return;
        }

        _allWidgets.Remove(widget);
        RefreshWidgetList();
        WidgetStore.Save(_allWidgets);
        NotifyTaskbarSettingsChanged();
    }

    private void AddWidgetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddWidgetWindow { Owner = this };

        if (dialog.ShowDialog() == true && dialog.ResultWidget is { } newWidget)
        {
            _allWidgets.Add(newWidget);
            RefreshWidgetList();
            WidgetStore.Save(_allWidgets);
            NotifyTaskbarSettingsChanged();
        }
    }

    private ObservableCollection<Tile> ListFor(TileKind kind) => kind switch
    {
        TileKind.Website => WebTiles,
        TileKind.Game => GameTiles,
        _ => AppTiles,
    };

    private void RefreshTileList(TileKind kind)
    {
        var list = ListFor(kind);
        list.Clear();
        foreach (var tile in _allTiles.Where(t => t.Kind == kind).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            list.Add(tile);
        }
    }

    private void RemoveTileButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Tile tile)
        {
            return;
        }

        _allTiles.Remove(tile);
        RefreshTileList(tile.Kind);
        _onTilesChanged();
    }

    private string _installedGameScanStatus = "";
    public string InstalledGameScanStatus
    {
        get => _installedGameScanStatus;
        set
        {
            _installedGameScanStatus = value;
            OnPropertyChanged();
        }
    }

    private async void ScanInstalledGamesButton_Click(object sender, RoutedEventArgs e)
    {
        InstalledGameScanStatus = "Scanning...";

        var found = await Task.Run(GameLibraryScanner.ScanAll);

        var existingTargets = new HashSet<string>(
            _allTiles.Where(t => t.Kind == TileKind.Game).Select(t => t.TargetPath),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var game in found)
        {
            if (!existingTargets.Add(game.TargetPath))
            {
                continue;
            }

            var tile = new Tile
            {
                Name = game.Name,
                TargetPath = game.TargetPath,
                Kind = TileKind.Game,
                IconSourcePath = game.TargetPath,
                Arguments = string.IsNullOrWhiteSpace(game.Arguments) ? null : game.Arguments,
            };

            _allTiles.Add(tile);
            _ = IconLoader.LoadIconAsync(tile, _settings);
            added++;
        }

        RefreshTileList(TileKind.Game);
        InstalledGameScanStatus = added == 0
            ? $"No new games found ({found.Count} already added or none detected)."
            : $"Added {added} game(s) from {found.Select(g => g.Source).Distinct().Count()} launcher(s).";

        if (added > 0)
        {
            _onTilesChanged();
        }
    }

    private void ScrapeButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Tile tile)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.IgdbClientId) || string.IsNullOrWhiteSpace(_settings.IgdbClientSecret))
        {
            System.Windows.MessageBox.Show(this, "Set an IGDB Client ID and Secret above first.", "Scrape Cover Art",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ScrapeCoverWindow(tile.Name, _settings.IgdbClientId, _settings.IgdbClientSecret) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedCoverPath is not { } coverPath)
        {
            return;
        }

        tile.CustomIconPath = coverPath;
        _ = IconLoader.LoadIconAsync(tile, _settings);
        _onTilesChanged();
    }

    private void RefreshAccentSwatches()
    {
        AccentSwatches.Clear();

        foreach (var hex in PresetAccentColors)
        {
            AccentSwatches.Add(new AccentSwatchItem
            {
                Hex = hex,
                Brush = HexToBrush(hex),
                IsCustom = false,
                IsSelected = string.Equals(hex, _settings.AccentColor, StringComparison.OrdinalIgnoreCase),
            });
        }

        var isCustomActive = !PresetAccentColors.Contains(_settings.AccentColor, StringComparer.OrdinalIgnoreCase);
        AccentSwatches.Add(new AccentSwatchItem
        {
            Hex = _settings.AccentColor,
            // Shows the active custom color once one is set; otherwise a neutral placeholder
            // behind the pencil icon, since there's no custom color to preview yet.
            Brush = isCustomActive ? HexToBrush(_settings.AccentColor) : Brushes.Transparent,
            IsCustom = true,
            IsSelected = isCustomActive,
        });
    }

    private static SolidColorBrush HexToBrush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AccentSwatchItem swatch)
        {
            return;
        }

        if (swatch.IsCustom)
        {
            var currentHex = swatch.IsSelected ? swatch.Hex : _settings.AccentColor;
            var dialog = new CustomColorWindow(currentHex) { Owner = this };

            if (dialog.ShowDialog() == true && dialog.Hex is { } chosenHex)
            {
                _settings.AccentColor = chosenHex;
                _onSettingsChanged();
                RefreshAccentSwatches();
            }

            return;
        }

        _settings.AccentColor = swatch.Hex;
        _onSettingsChanged();
        RefreshAccentSwatches();
    }

    private void ExitGridShell_Click(object sender, RoutedEventArgs e)
    {
        if (KioskModeEnabled)
        {
            var dialog = new KioskPinWindow(_settings.KioskModePinHash!, "Enter your PIN to exit GridShell.") { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }
        }

        // GridShell runs as two processes (this grid + the always-running taskbar) — signal
        // the taskbar to close too, not just this window, so "Exit" actually exits GridShell.
        // Not Process.CloseMainWindow() — that silently no-ops for the taskbar's window
        // (ShowInTaskbar="False" sets WS_EX_TOOLWINDOW, which MainWindowHandle resolution skips).
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("GridShell"))
        {
            ProcessWindowCloser.CloseAnyWindowOf(process.Id);
        }

        Close();
        Owner?.Close();
    }
}
