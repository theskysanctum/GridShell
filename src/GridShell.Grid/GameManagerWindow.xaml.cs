using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GridShell.Core;
using GridShell.Core.Controllers;
using GridShell.Core.GameLibraries;
using GridShell.Core.Interop;
using GridShell.Core.Store;
using GridShell.Core.Tiles;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using AppSettings = GridShell.Core.Settings.AppSettings;

namespace GridShell;

/// <summary>Consolidates everything about games into one place - IGDB cover art, the
/// Steam/Epic/GOG installed-game scanner, the game tile list, RetroAchievements, ROM folders,
/// per-platform emulator configuration, and controller management (virtual Xbox360 pads via
/// ViGEm + hiding the original HID device via HidHide) - moved here from Settings' old
/// "Tiles > Games" and "Emulation" tabs, and from the standalone Controller Manager window,
/// which this replaces entirely. No install button for emulators here: they install through the
/// Store (WingetInstaller/GitHubReleaseInstaller), which already auto-wires EmulatorConfigs on
/// success (EmulatorAutoWire) - this window just reflects whatever the Store already set up,
/// plus lets you browse to one manually for anything the Store doesn't cover.</summary>
public partial class GameManagerWindow : Window, INotifyPropertyChanged
{
    private readonly List<Tile> _allTiles;
    private readonly AppSettings _settings;
    private readonly Action _onTilesChanged;
    private readonly Action _onSettingsChanged;

    private readonly DispatcherTimer _controllerRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _controllerFeedTimer = new() { Interval = TimeSpan.FromMilliseconds(8) };

    private string _igdbClientId = "";

    public GameManagerWindow(List<Tile> allTiles, AppSettings settings, Action onTilesChanged, Action onSettingsChanged)
    {
        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.HideWindowIcon();
        this.ScaleToWorkArea(widthFraction: 0.6, heightFraction: 0.92,
            minWidth: 560, minHeight: 600, maxWidth: 800, maxHeight: 980);

        _allTiles = allTiles;
        _settings = settings;
        _onTilesChanged = onTilesChanged;
        _onSettingsChanged = onSettingsChanged;

        Loaded += GameManagerWindow_Loaded;
        Closed += GameManagerWindow_Closed;
    }

    private async void GameManagerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _igdbClientId = _settings.IgdbClientId ?? "";
        OnPropertyChanged(nameof(IgdbClientId));
        IgdbClientSecretBox.Password = _settings.IgdbClientSecret ?? "";

        RefreshGameTiles();

        // Mutates the same _settings instance this window already holds (rather than
        // RetroArchCoreAutoWire's own load/save cycle) so a core it just wired in shows up
        // immediately below, instead of only after this window is reopened.
        if (await RetroArchCoreAutoWire.TryWireAllAsync(_settings))
        {
            _onSettingsChanged();
        }

        // Every platform's own slug subfolder, ready-made under the default ROMs root and
        // every extra folder already configured - covers extra folders added in an earlier
        // session, before this existed, too. New extra folders get the same treatment in
        // AddRomPoolButton_Click below.
        EmulationPlatforms.EnsureSlugFoldersExist(EmulationPlatforms.RomsRootPath);
        foreach (var extraRoot in _settings.ExtraRomPaths)
        {
            EmulationPlatforms.EnsureSlugFoldersExist(extraRoot);
        }

        LoadEmulatorPlatforms();

        // Both processes read controllers via PadReader - whitelisting both up front means
        // hiding a device's raw HID interface(s) never blocks GridShell's own feed loop,
        // regardless of which process happens to own the active virtual pad for it.
        if (HidHideBridge.IsInstalled())
        {
            TryRegisterSelfAndSibling();
        }

        _controllerRefreshTimer.Tick += (_, _) => RefreshDevices();
        _controllerRefreshTimer.Start();
        RefreshDevices();

        _controllerFeedTimer.Tick += (_, _) => FeedActiveVirtualPads();
        _controllerFeedTimer.Start();
    }

    private void GameManagerWindow_Closed(object? sender, EventArgs e)
    {
        _controllerRefreshTimer.Stop();
        _controllerFeedTimer.Stop();
        foreach (var device in Devices)
        {
            device.StopVirtualPad();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // --- Games: IGDB, installed-game scan, game tile list -----------------------------------

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

    public ObservableCollection<Tile> GameTiles { get; } = new();

    private void RefreshGameTiles()
    {
        GameTiles.Clear();
        foreach (var tile in _allTiles.Where(t => t.Kind == TileKind.Game).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            GameTiles.Add(tile);
        }
    }

    private void RemoveTileButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Tile tile)
        {
            return;
        }

        _allTiles.Remove(tile);
        RefreshGameTiles();
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

    /// <summary>Same button now does both halves of "find games already on disk": installed
    /// games from Steam/Epic/GOG (GameLibraryScanner) AND ROMs sitting under each configured
    /// platform's ROM folder(s) (RomScanner) - previously ROM folders were only ever counted for
    /// display (EmulatorPlatformRow.RomCountText), never actually turned into tiles; adding one
    /// for each ROM found was a manual, one-at-a-time AddTileWindow step until now.</summary>
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

        // Snapshotted here, on the UI thread, before handing off to the background Task.Run
        // below - _allTiles and ExtraRomPaths (a UI-bound ObservableCollection) aren't safe to
        // read from a background thread while the user could still be interacting with the
        // window mid-scan.
        var existingGameKeys = new HashSet<(string Platform, string Arguments)>(
            _allTiles.Where(t => t.Kind == TileKind.Game && t.Platform is not null)
                .Select(t => (t.Platform!, t.Arguments ?? "")));
        var extraRomPaths = ExtraRomPaths.ToList();

        var romTiles = await Task.Run(() => BuildRomTiles(existingGameKeys, extraRomPaths));
        foreach (var tile in romTiles)
        {
            _allTiles.Add(tile);
            _ = IconLoader.LoadIconAsync(tile, _settings);
            added++;
        }

        RefreshGameTiles();
        InstalledGameScanStatus = added == 0
            ? $"No new games found ({found.Count} installed-game(s)/ROM(s) already added or none detected)."
            : $"Added {added} game(s) from {found.Select(g => g.Source).Distinct().Count()} launcher(s) and ROM folders.";

        if (added > 0)
        {
            _onTilesChanged();
        }
    }

    /// <summary>One Tile per ROM file under every platform that has a configured emulator -
    /// EmulatorConfig.ArgumentTemplate's "{rom}" gets resolved to that exact file right now
    /// (matching AddTileWindow's own one-at-a-time flow), not re-resolved at launch, so a tile
    /// keeps pointing at the same ROM even if the emulator config changes later. Deduplicated by
    /// (platform, resolved arguments) - the same key a repeat scan would produce for a ROM
    /// already added, rather than by TargetPath alone (shared by every ROM under one emulator).
    /// existingGameKeys/extraRomPaths are snapshotted by the caller before this runs on a
    /// background thread - this method itself only touches EmulationPlatforms.All (static) and
    /// RomScanner's own file I/O, never _allTiles/ExtraRomPaths directly.</summary>
    private List<Tile> BuildRomTiles(HashSet<(string Platform, string Arguments)> existingGameKeys, List<string> extraRomPaths)
    {
        var newTiles = new List<Tile>();

        foreach (var platform in EmulationPlatforms.All)
        {
            if (!_settings.EmulatorConfigs.TryGetValue(platform.Slug, out var config) ||
                string.IsNullOrWhiteSpace(config.EmulatorPath))
            {
                continue;
            }

            foreach (var romPath in RomScanner.GetRoms(platform, extraRomPaths))
            {
                var arguments = config.ArgumentTemplate.Replace("{rom}", romPath);
                if (!existingGameKeys.Add((platform.Slug, arguments)))
                {
                    continue;
                }

                newTiles.Add(new Tile
                {
                    Name = RomTitleCleaner.Clean(System.IO.Path.GetFileNameWithoutExtension(romPath)),
                    TargetPath = config.EmulatorPath,
                    Kind = TileKind.Game,
                    IconSourcePath = config.EmulatorPath,
                    Arguments = arguments,
                    Platform = platform.Slug,
                });
            }
        }

        return newTiles;
    }

    private void ScrapeButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Tile tile)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.IgdbClientId) || string.IsNullOrWhiteSpace(_settings.IgdbClientSecret))
        {
            MessageBox.Show(this, "Set an IGDB Client ID and Secret above first.", "Scrape Cover Art",
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

    // --- Emulators: RetroAchievements, ROM folders, per-platform config ---------------------

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

        EmulationPlatforms.EnsureSlugFoldersExist(path);

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
            MessageBox.Show(
                this, $"Couldn't open {row.DisplayName}'s emulator.", "Launch failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RomFolderPathButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string path)
        {
            return;
        }

        // Should already exist (EnsureSlugFoldersExist runs on load and on every new extra
        // folder), but cheap/idempotent to make sure rather than failing to open a folder that
        // genuinely isn't there yet for some other reason.
        System.IO.Directory.CreateDirectory(path);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"")
        {
            UseShellExecute = true,
        });
    }

    // --- Controllers: virtual Xbox360 pads (ViGEm) + hiding the original device (HidHide) --

    public ObservableCollection<ControllerEntry> Devices { get; } = new();

    public Visibility NoDevicesVisibility => Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
                "Controllers", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this, $"HidHide operation failed:\n\n{ex.Message}", "Controllers",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

/// <summary>One row in the Controllers tab's list - wraps a PadDevice with the mutable
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

    /// <summary>Quick-access toggle, right in the Controllers tab's own list - writes straight
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
