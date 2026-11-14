using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridShell.Core;
using GridShell.Core.GameLibraries;
using GridShell.Core.Interop;
using GridShell.Core.Interop.Audio;
using GridShell.Core.Interop.Bluetooth;
using GridShell.Core.Interop.Display;
using GridShell.Core.Interop.WiFi;
using GridShell.Core.Store;
using GridShell.Core.Tiles;
using GridShell.Core.Updates;
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
    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;

    private bool _autostartEnabled;
    private DetectedBrowser? _selectedDefaultBrowser;
    private TileSizeOption _tileSize;
    private AppTheme _theme;
    private UiFontChoice _uiFont;
    private string? _startButtonIconImagePath;
    private bool _useGridShellAsSystemShell;
    private bool _wallpaperMuted;

    private bool _isCheckingForUpdate;
    private bool _isUpdateAvailable;
    private bool _isDownloadingUpdate;
    private double _updateDownloadProgress;
    private string _updateStatusText = $"Version {UpdateChecker.CurrentVersion}";
    private string? _latestReleaseUrl;

    private static readonly string WatchdogExePath = SiblingExePath.Resolve("GridShell.Watchdog", "GridShell.Watchdog.exe");

    public ObservableCollection<DetectedBrowser> Browsers { get; } = new();
    public ObservableCollection<StoreSceneViewModel> Scenes { get; } = new();

    private static readonly string[] PresetAccentColors =
        { "#F0AD4E", "#D9534F", "#5CB85C", "#4A90D9", "#9B59B6", "#1ABC9C" };

    public ObservableCollection<AccentSwatchItem> AccentSwatches { get; } = new();

    private static readonly (string Codepoint, string Name)[] StartIconChoices =
    {
        ("e5c3", "Apps"),
        ("e9b0", "Grid"),
        ("e871", "Dashboard"),
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

        var paired = await BluetoothDeviceManager.PairAsync(device.Id);

        if (paired)
        {
            DiscoveredBluetoothDevices.Remove(device);
            await RefreshBluetoothAsync();
        }
        else
        {
            new ToastWindow($"Couldn't pair with {device.Name}.", TimeSpan.FromSeconds(5), isWarning: true).Show();
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
            new ToastWindow($"Couldn't connect to {profileName}.", TimeSpan.FromSeconds(5), isWarning: true).Show();
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
            new ToastWindow($"Couldn't connect to {network.Ssid}.", TimeSpan.FromSeconds(5), isWarning: true).Show();
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

    private void LaunchInstallerAndExit(string installerPath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true });

        foreach (var process in System.Diagnostics.Process.GetProcessesByName("GridShell"))
        {
            foreach (var hwnd in ProcessWindowCloser.FindWindowsOf(process.Id))
            {
                PostMessage(hwnd, PrepareForUpdateMessage.Id, IntPtr.Zero, IntPtr.Zero);
            }

            ProcessWindowCloser.CloseAnyWindowOf(process.Id);
        }

        Application.Current.Shutdown();
    }

    private void RealDebridApiTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _settings.RealDebridApiToken = RealDebridApiTokenBox.Password;
        _onSettingsChanged();
    }

    public string? DownloadCookies
    {
        get => _settings.DownloadCookies;
        set
        {
            _settings.DownloadCookies = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
            _onSettingsChanged();
        }
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

    public bool CanEditAppearance => !KioskModeEnabled;

    public string KioskModeButtonText => KioskModeEnabled ? "Disable Kiosk Mode..." : "Enable Kiosk Mode...";

    public bool KioskModeEnabled
    {
        get => _settings.KioskModeEnabled;
        private set
        {
            _settings.KioskModeEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KioskModeButtonText));
            OnPropertyChanged(nameof(CanEditAppearance));
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

    private async Task LoadScenesAsync()
    {
        var scenes = (await StoreSceneSync.FetchCatalogAsync()).Scenes
            .Where(s => StoreSceneSync.IsDownloaded(s.EntryFile))
            .Select(s => new StoreSceneViewModel { Scene = s })
            .ToList();

        foreach (var scene in scenes)
        {
            SetSceneThumbnail(scene);
            RefreshSceneIsCurrent(scene);
        }

        Scenes.Clear();
        foreach (var scene in scenes)
        {
            Scenes.Add(scene);
        }
    }

    private static void SetSceneThumbnail(StoreSceneViewModel scene)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(StoreSceneSync.GetThumbnailUrl(scene.Scene.Id), UriKind.Absolute);
        bitmap.EndInit();
        scene.Thumbnail = bitmap;
    }

    private void RefreshSceneIsCurrent(StoreSceneViewModel scene)
    {
        var expectedPath = StoreSceneSync.GetLocalCachePath(scene.Scene.EntryFile);
        scene.IsCurrent = string.Equals(_settings.WallpaperUrl, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySceneButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not StoreSceneViewModel scene)
        {
            return;
        }

        _settings.WallpaperUrl = StoreSceneSync.GetLocalCachePath(scene.Scene.EntryFile);
        _onSettingsChanged();
        NotifyTaskbarSettingsChanged();

        foreach (var s in Scenes)
        {
            RefreshSceneIsCurrent(s);
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

#pragma warning disable WPF0001
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
            new ToastWindow("That file doesn't look like a usable font.", TimeSpan.FromSeconds(5), isWarning: true).Show();
            return;
        }

        _settings.CustomFontPath = dialog.FileName;
        OnPropertyChanged(nameof(CustomFontPath));
        UiFont = UiFontChoice.Custom;
    }

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

    private static void NotifyTaskbarSettingsChanged() => SettingsChangedMessage.NotifyTaskbar();

    public SettingsWindow(AppSettings settings, Action onSettingsChanged)
    {
        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.HideWindowIcon();
        this.ScaleToWorkArea(widthFraction: 0.6, heightFraction: 0.92,
            minWidth: 520, minHeight: 560, maxWidth: 760, maxHeight: 940);

        _settings = settings;
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

        RealDebridApiTokenBox.Password = _settings.RealDebridApiToken ?? "";
        OnPropertyChanged(nameof(DownloadCookies));
        DownloadCookies = _settings.DownloadCookies ?? "";
        OnPropertyChanged(nameof(DownloadManagerFolder));

        _wallpaperMuted = _settings.WallpaperMuted;
        OnPropertyChanged(nameof(WallpaperMuted));

        _ = LoadScenesAsync();

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

        CheckForUpdatesButton_Click(this, new RoutedEventArgs());
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

    private void ReportBugButton_Click(object sender, RoutedEventArgs e) =>
        new ReportBugWindow { Owner = this }.ShowDialog();

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

        foreach (var process in System.Diagnostics.Process.GetProcessesByName("GridShell"))
        {
            ProcessWindowCloser.CloseAnyWindowOf(process.Id);
        }

        Close();
        Owner?.Close();
    }
}
