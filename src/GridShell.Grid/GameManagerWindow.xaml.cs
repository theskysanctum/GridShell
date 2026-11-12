using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using GridShell.Core;
using GridShell.Core.GameLibraries;
using GridShell.Core.Interop;
using GridShell.Core.Tiles;
using AppSettings = GridShell.Core.Settings.AppSettings;

namespace GridShell;

/// <summary>Consolidates everything about games into one place - IGDB cover art, the
/// Steam/Epic/GOG installed-game scanner, the game tile list, RetroAchievements, ROM folders,
/// and per-platform emulator configuration - moved here from Settings' old "Tiles > Games" and
/// "Emulation" tabs, which this replaces entirely. No install button here: emulators install
/// through the Store (WingetInstaller/GitHubReleaseInstaller), which already auto-wires
/// EmulatorConfigs on success (EmulatorAutoWire) - this window just reflects whatever the Store
/// already set up, plus lets you browse to one manually for anything the Store doesn't cover.</summary>
public partial class GameManagerWindow : Window, INotifyPropertyChanged
{
    private readonly List<Tile> _allTiles;
    private readonly AppSettings _settings;
    private readonly Action _onTilesChanged;
    private readonly Action _onSettingsChanged;

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
    }

    private void GameManagerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _igdbClientId = _settings.IgdbClientId ?? "";
        OnPropertyChanged(nameof(IgdbClientId));
        IgdbClientSecretBox.Password = _settings.IgdbClientSecret ?? "";

        RefreshGameTiles();
        LoadEmulatorPlatforms();
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

        RefreshGameTiles();
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
            MessageBox.Show(
                this, $"Couldn't open {row.DisplayName}'s emulator.", "Launch failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
