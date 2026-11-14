using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GridShell.Core.Interop;
using GridShell.Core.Settings;
using GridShell.Core.Store;
using GridShell.Core.Tiles;
using Vanara.PInvoke;

namespace GridShell;

/// <summary>GridShell's app store - a curated catalog (synced from GridShell's own repo via
/// StoreCatalogSync, so it can be updated without a GridShell release) installed through winget
/// (WingetInstaller) for most apps, or GitHubReleaseInstaller for the few with no winget package
/// but a real downloadable GitHub release (StoreApp.InstallMethod picks which). Theme-aware
/// throughout, same DynamicResource brushes as every other window.</summary>
public partial class StoreWindow : Window, INotifyPropertyChanged
{
    private const string ScenesCategoryLabel = "Scenes";

    private readonly List<Tile> _allTiles;
    private readonly AppSettings _settings;
    private readonly Action _onTilesChanged;

    private List<StoreAppViewModel> _allApps = new();
    private List<StoreSceneViewModel> _allScenes = new();

    public ObservableCollection<StoreAppViewModel> Apps { get; } = new();
    public ObservableCollection<StoreSceneViewModel> Scenes { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public bool IsScenesCategory => SelectedCategory == ScenesCategoryLabel;

    private string _selectedCategory = "";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsScenesCategory));
            ApplyFilter();
        }
    }

    private string _catalogStatusText = "Loading catalog...";
    public string CatalogStatusText
    {
        get => _catalogStatusText;
        set
        {
            _catalogStatusText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public StoreWindow(List<Tile> allTiles, AppSettings settings, Action onTilesChanged)
    {
        InitializeComponent();
        DataContext = this;

        _allTiles = allTiles;
        _settings = settings;
        _onTilesChanged = onTilesChanged;

        // WPF-property sizing for the brief window before the HWND exists -
        // Window_SourceInitialized takes over with the authoritative native version right
        // after, same two-step MainWindow already uses for the same reason (SystemParameters.
        // WorkArea is WPF's own cached, DPI-scaled copy, good enough only for this first paint).
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = 0;
        Width = workArea.Width;
        Height = SystemParameters.PrimaryScreenHeight;

        Loaded += StoreWindow_Loaded;
    }

    /// <summary>Sizes to the real screen minus GridShell's own taskbar sidebar - a real AppBar
    /// reservation (AppBarManager), not the fake "maximized" the Store used to rely on, which
    /// only respected the real Windows taskbar (hidden for GridShell's whole lifetime) and had
    /// no idea GridShell's own sidebar existed.</summary>
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var workArea = AppBarManager.GetWorkArea();
        var screenWidth = User32.GetSystemMetrics(User32.SystemMetric.SM_CXSCREEN);
        var screenHeight = User32.GetSystemMetrics(User32.SystemMetric.SM_CYSCREEN);

        User32.SetWindowPos(hwnd, HWND.NULL, workArea.left, 0, screenWidth - workArea.left, screenHeight,
            User32.SetWindowPosFlags.SWP_NOZORDER | User32.SetWindowPosFlags.SWP_NOACTIVATE);
    }

    private async void StoreWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var catalog = await StoreCatalogSync.SyncAndLoadAsync();
        _allApps = catalog.Apps
            .OrderBy(a => a.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(a => new StoreAppViewModel { App = a })
            .ToList();

        foreach (var app in _allApps)
        {
            SetBundledIconIfAny(app);
        }

        CatalogStatusText = _allApps.Count == 0
            ? "Couldn't load the catalog - check your internet connection and reopen the Store."
            : $"{_allApps.Count} apps available";

        // Fetched directly over HTTP (StoreSceneSync), not through the app-catalog clone above -
        // Scenes are a deliberately separate, on-demand download. Downloading is all the Store
        // does with them - Applying one as the wallpaper happens in Settings' Appearance tab,
        // same split as "install" (Store) vs. "configure" (Settings) everywhere else.
        _allScenes = (await StoreSceneSync.FetchCatalogAsync()).Scenes
            .Select(s => new StoreSceneViewModel { Scene = s })
            .ToList();

        foreach (var scene in _allScenes)
        {
            SetSceneThumbnail(scene);
            scene.IsDownloaded = StoreSceneSync.IsDownloaded(scene.Scene.EntryFile);
        }

        Categories.Clear();
        foreach (var category in _allApps.Select(a => a.Category).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
        {
            Categories.Add(category);
        }

        // Scenes aren't StoreApps, so they're not among _allApps' own categories - appended as
        // its own fixed last entry instead, same left-rail category list either way.
        if (_allScenes.Count > 0)
        {
            Categories.Add(ScenesCategoryLabel);
        }

        // No catch-all "All Apps" entry - land on the first real category instead, now that
        // Categories actually has entries for the ListBox's SelectedItem binding to match against.
        if (Categories.Count > 0)
        {
            SelectedCategory = Categories[0];
        }

        _ = RefreshInstalledStateAsync();
    }

    /// <summary>Opening the Store previously always showed every app as not-installed, since
    /// each StoreAppViewModel starts that way and nothing checked reality against it - fixed by
    /// querying winget's own "already installed" check (WingetInstaller.IsInstalledAsync, same
    /// `winget list --id X -e` check InstallAsync uses) for every catalog entry on load.
    /// Throttled to a handful at a time rather than firing 20+ winget.exe processes at once. Also
    /// backfills an Apps-tab tile (StoreTileAutoWire) for anything already installed before this
    /// existed, or installed outside the Store - not just newly-installed apps - so opening the
    /// Store once picks up everything already on the machine.</summary>
    private async Task RefreshInstalledStateAsync()
    {
        using var throttle = new SemaphoreSlim(4);
        var tilesChanged = false;
        var tilesLock = new object();

        var checks = _allApps.Select(async app =>
        {
            await throttle.WaitAsync();
            try
            {
                app.State = StoreAppState.Checking;
                var installed = app.App.InstallMethod switch
                {
                    "github" => GitHubReleaseInstaller.IsInstalled(app.App.Id),
                    "package" => PackageInstaller.IsInstalled(app.App.Id),
                    "direct" => DirectInstaller.IsInstalled(app.App),
                    "browser" => BrowserInstaller.IsInstalled(app.App),
                    _ => await WingetInstaller.IsInstalledAsync(app.App),
                };
                app.State = installed ? StoreAppState.Installed : StoreAppState.NotInstalled;

                if (installed)
                {
                    Tile? newTile;
                    lock (tilesLock)
                    {
                        newTile = StoreTileAutoWire.TryWire(app.App, _allTiles);
                    }

                    if (newTile is not null)
                    {
                        tilesChanged = true;
                        _ = IconLoader.LoadIconAsync(newTile, _settings);
                    }
                }
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(checks);

        if (tilesChanged)
        {
            _onTilesChanged();
        }
    }

    /// <summary>The catalog's own bundled logo (store-catalog/icons/{id}.png, synced alongside
    /// catalog.json - see StoreCatalogSync.GetIconPath) - shown immediately, whether or not the
    /// app is installed, since there's no exe to extract an icon from before then. Catalog
    /// entries without a bundled icon keep the InitialLetter fallback square.</summary>
    private static void SetBundledIconIfAny(StoreAppViewModel app)
    {
        var iconPath = StoreCatalogSync.GetIconPath(app.App.Id);
        if (iconPath is null)
        {
            return;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        app.Icon = bitmap;
    }

    /// <summary>Scenes' own thumbnail, loaded straight from raw.githubusercontent.com (PNGs
    /// don't hit the text/plain-forcing behavior .html files do - see StoreSceneSync) - WPF's
    /// BitmapImage downloads a remote UriSource on its own in the background, no manual fetch
    /// needed.</summary>
    private static void SetSceneThumbnail(StoreSceneViewModel scene)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(StoreSceneSync.GetThumbnailUrl(scene.Scene.Id), UriKind.Absolute);
        bitmap.EndInit();
        scene.Thumbnail = bitmap;
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text;

        if (IsScenesCategory)
        {
            Apps.Clear();
            Scenes.Clear();
            foreach (var scene in _allScenes.Where(s => string.IsNullOrWhiteSpace(query)
                || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                Scenes.Add(scene);
            }

            return;
        }

        // A search query searches every app regardless of the selected category - there's no
        // catch-all "All Apps" category to switch to first, so search would otherwise only ever
        // match within whichever single category happens to be selected.
        IEnumerable<StoreAppViewModel> matches = string.IsNullOrWhiteSpace(query)
            ? _allApps.Where(a => a.Category == SelectedCategory)
            : _allApps.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Category.Contains(query, StringComparison.OrdinalIgnoreCase));

        Scenes.Clear();
        Apps.Clear();
        foreach (var app in matches)
        {
            Apps.Add(app);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    /// <summary>Downloads a scene's entry HTML into the local cache - that's all the Store does
    /// with it. Applying it as the wallpaper happens in Settings' Appearance tab, which only
    /// lists scenes already downloaded here.</summary>
    private async void DownloadSceneButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not StoreSceneViewModel scene)
        {
            return;
        }

        scene.IsDownloading = true;
        var entryPath = await StoreSceneSync.DownloadEntryAsync(scene.Scene.EntryFile);
        scene.IsDownloading = false;

        if (entryPath is null)
        {
            MessageBox.Show(this, $"Couldn't download {scene.Name} - check your internet connection.",
                "Scenes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        scene.IsDownloaded = true;
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not StoreAppViewModel app)
        {
            return;
        }

        // Doesn't fit the Installed/Failed shape every other method's result does - there's no
        // way to know whether the user actually installed anything, just whether the browser
        // opened - so this is its own case entirely rather than forced through that switch.
        if (app.App.InstallMethod == "browser")
        {
            new ToastWindow(
                BrowserInstaller.Open(app.App)
                    ? $"Opened {app.Name}'s download page - install it yourself, then reopen the Store to pick it up."
                    : $"Couldn't open {app.Name}'s download page.",
                TimeSpan.FromSeconds(5)).Show();
            return;
        }

        app.State = StoreAppState.Installing;
        var result = app.App.InstallMethod switch
        {
            "github" => await GitHubReleaseInstaller.InstallAsync(app.App),
            "package" => await PackageInstaller.InstallAsync(app.App),
            "direct" => await DirectInstaller.InstallAsync(app.App),
            _ => await WingetInstaller.InstallAsync(app.App),
        };

        app.State = result.Outcome switch
        {
            InstallOutcome.Installed or InstallOutcome.AlreadyInstalled => StoreAppState.Installed,
            _ => StoreAppState.Failed,
        };

        if (app.State == StoreAppState.Failed)
        {
            app.ErrorMessage = result.Message;
        }
        else
        {
            new ToastWindow($"{app.Name} installed.", TimeSpan.FromSeconds(5)).Show();
            EmulatorAutoWire.TryWire(app.App);

            // RetroArch itself has no fixed platformSlugs (EmulatorAutoWire's one-exe-one-
            // template model doesn't fit it - each platform needs its own "-L <core>" flag) -
            // RetroArchCoreAutoWire handles that separately once it's actually installed.
            if (app.App.Id == "libretro.retroarch")
            {
                await RetroArchCoreAutoWire.TryWireAllAsync();
            }

            if (StoreTileAutoWire.TryWire(app.App, _allTiles) is { } newAppTile)
            {
                _ = IconLoader.LoadIconAsync(newAppTile, _settings);
                _onTilesChanged();
            }
        }
    }

    /// <summary>Removes the matching Apps-tab tile (found before uninstalling, while its exe
    /// still exists to resolve against) and hands off to whichever installer actually put the
    /// app on disk - winget for a real package, a plain folder delete for github/package
    /// installs that have no real installer to call.</summary>
    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not StoreAppViewModel app)
        {
            return;
        }

        // GridShell didn't install this one (BrowserInstaller just opened a download page),
        // so it has no installer-specific knowledge to safely silently uninstall it either -
        // same reasoning that's kept winget's own uninstall from guessing the wrong switch for
        // an installer technology it doesn't actually know. Opens Windows' own Apps & Features
        // page instead of attempting anything.
        if (app.App.InstallMethod == "browser")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
            new ToastWindow($"Opened Apps & Features - uninstall {app.Name} from there.", TimeSpan.FromSeconds(5)).Show();
            return;
        }

        var wiredTile = StoreTileAutoWire.FindWiredTile(app.App, _allTiles);
        app.State = StoreAppState.Uninstalling;

        var result = app.App.InstallMethod switch
        {
            "github" => GitHubReleaseInstaller.Uninstall(app.App.Id),
            "package" => PackageInstaller.Uninstall(app.App.Id),
            "direct" => await DirectInstaller.UninstallAsync(app.App),
            _ => await WingetInstaller.UninstallAsync(app.App),
        };

        if (result.Outcome == UninstallOutcome.Uninstalled)
        {
            app.State = StoreAppState.NotInstalled;
            new ToastWindow($"{app.Name} uninstalled.", TimeSpan.FromSeconds(5)).Show();

            if (wiredTile is not null)
            {
                _allTiles.Remove(wiredTile);
                _onTilesChanged();
            }
        }
        else
        {
            app.State = StoreAppState.Failed;
            app.ErrorMessage = result.Message;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
