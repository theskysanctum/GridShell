using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GridShell.Core.Interop;
using GridShell.Core.Tiles;

namespace GridShell;

public partial class AddTileWindow : Window, INotifyPropertyChanged
{
    private List<Tile> _allApps = new();
    private string _appSearchText = "";
    private Tile? _selectedApp;
    private bool _isLoadingApps = true;
    private string _websiteUrl = "";
    private string _websiteName = "";
    private DetectedBrowser? _selectedBrowser;
    private string _manualAppTitle = "";
    private string _manualAppPath = "";
    private string _gameTitle = "";
    private string _gamePath = "";
    private bool _isEmulatedGame;
    private string _emulatedGameTitle = "";
    private string _romPath = "";
    private EmulationPlatform? _selectedPlatform;

    public ObservableCollection<Tile> FilteredApps { get; } = new();
    public ObservableCollection<DetectedBrowser> Browsers { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string AppSearchText
    {
        get => _appSearchText;
        set
        {
            if (_appSearchText == value) return;
            _appSearchText = value;
            OnPropertyChanged();
            ApplyAppFilter();
        }
    }

    public Tile? SelectedApp
    {
        get => _selectedApp;
        set
        {
            _selectedApp = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoadingApps
    {
        get => _isLoadingApps;
        set
        {
            _isLoadingApps = value;
            OnPropertyChanged();
        }
    }

    public string WebsiteUrl
    {
        get => _websiteUrl;
        set { _websiteUrl = value; OnPropertyChanged(); }
    }

    public string WebsiteName
    {
        get => _websiteName;
        set { _websiteName = value; OnPropertyChanged(); }
    }

    public DetectedBrowser? SelectedBrowser
    {
        get => _selectedBrowser;
        set { _selectedBrowser = value; OnPropertyChanged(); }
    }

    public bool NoBrowsersFound => Browsers.Count == 0;

    public string ManualAppTitle
    {
        get => _manualAppTitle;
        set { _manualAppTitle = value; OnPropertyChanged(); }
    }

    public string ManualAppPath
    {
        get => _manualAppPath;
        set { _manualAppPath = value; OnPropertyChanged(); }
    }

    public string GameTitle
    {
        get => _gameTitle;
        set { _gameTitle = value; OnPropertyChanged(); }
    }

    public string GamePath
    {
        get => _gamePath;
        set { _gamePath = value; OnPropertyChanged(); }
    }

    public ObservableCollection<EmulationPlatform> ConfiguredPlatforms { get; } = new();

    public Dictionary<string, EmulatorConfig> EmulatorConfigs { get; set; } = new();

    /// <summary>Which of Native/Emulated the Game tab shows — a toggle within the Game section
    /// itself, same spirit as RequestedKind but one level down, so a native game's fields can
    /// never be submitted as an emulated one or vice versa.</summary>
    public bool IsEmulatedGame
    {
        get => _isEmulatedGame;
        set
        {
            _isEmulatedGame = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNativeGame));
        }
    }

    public bool IsNativeGame => !IsEmulatedGame;

    public string EmulatedGameTitle
    {
        get => _emulatedGameTitle;
        set { _emulatedGameTitle = value; OnPropertyChanged(); }
    }

    public string RomPath
    {
        get => _romPath;
        set { _romPath = value; OnPropertyChanged(); }
    }

    public EmulationPlatform? SelectedPlatform
    {
        get => _selectedPlatform;
        set { _selectedPlatform = value; OnPropertyChanged(); }
    }

    public Tile? ResultTile { get; private set; }

    private TileKind _requestedKind = TileKind.App;
    private bool _pauseWallpaperWhileRunning;

    /// <summary>Which single kind of tile this dialog adds — set once, before showing, by
    /// whichever "+" the user clicked. There's no way to change it from within the dialog,
    /// so a kind's own fields can never be submitted under a different one.</summary>
    public TileKind RequestedKind
    {
        get => _requestedKind;
        set
        {
            _requestedKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAppKind));
            OnPropertyChanged(nameof(IsWebsiteKind));
            OnPropertyChanged(nameof(IsGameKind));
            OnPropertyChanged(nameof(HeaderText));

            // Defaults on for games (the main motivating case - "stop when anything in games"),
            // off otherwise. Still a plain checkbox the user can flip either way.
            PauseWallpaperWhileRunning = value == TileKind.Game;
        }
    }

    /// <summary>Only shown/wired on the Game tabs - see Tile.PauseWallpaperWhileRunning.</summary>
    public bool PauseWallpaperWhileRunning
    {
        get => _pauseWallpaperWhileRunning;
        set { _pauseWallpaperWhileRunning = value; OnPropertyChanged(); }
    }

    public bool IsAppKind => RequestedKind == TileKind.App;
    public bool IsWebsiteKind => RequestedKind == TileKind.Website;
    public bool IsGameKind => RequestedKind == TileKind.Game;

    public string HeaderText => RequestedKind switch
    {
        TileKind.Website => "Add Website",
        TileKind.Game => "Add Game",
        _ => "Add App",
    };

    public string? DefaultBrowserExecutablePath { get; set; }

    public AddTileWindow()
    {
        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();
        this.HideWindowIcon();
        Loaded += AddTileWindow_Loaded;
    }

    private async void AddTileWindow_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var browser in BrowserDetector.DetectInstalled())
        {
            Browsers.Add(browser);
        }
        OnPropertyChanged(nameof(NoBrowsersFound));
        SelectedBrowser = Browsers.FirstOrDefault(b => b.ExecutablePath == DefaultBrowserExecutablePath)
            ?? Browsers.FirstOrDefault();

        foreach (var platform in EmulationPlatforms.All)
        {
            if (EmulatorConfigs.TryGetValue(platform.Slug, out var config) &&
                !string.IsNullOrWhiteSpace(config.EmulatorPath))
            {
                ConfiguredPlatforms.Add(platform);
            }
        }
        SelectedPlatform = ConfiguredPlatforms.FirstOrDefault();

        _allApps = await Task.Run(() => AppTileEnumerator.EnumerateAppTiles().ToList());
        IsLoadingApps = false;
        ApplyAppFilter();
    }

    private void ApplyAppFilter()
    {
        var matches = string.IsNullOrWhiteSpace(AppSearchText)
            ? _allApps
            : _allApps.Where(a => a.Name.Contains(AppSearchText, StringComparison.OrdinalIgnoreCase));

        FilteredApps.Clear();
        foreach (var app in matches)
        {
            FilteredApps.Add(app);
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null) return;
        ResultTile = SelectedApp;
        DialogResult = true;
        Close();
    }

    private void AppList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedApp is null) return;
        ResultTile = SelectedApp;
        DialogResult = true;
        Close();
    }

    private void BrowseManualAppPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Choose a program",
        };

        if (dialog.ShowDialog(this) == true)
        {
            ManualAppPath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(ManualAppTitle))
            {
                ManualAppTitle = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void AddManualApp_Click(object sender, RoutedEventArgs e)
    {
        var path = ManualAppPath.Trim();
        if (path.Length == 0) return;

        var name = string.IsNullOrWhiteSpace(ManualAppTitle)
            ? System.IO.Path.GetFileNameWithoutExtension(path)
            : ManualAppTitle.Trim();

        ResultTile = new Tile
        {
            Name = name,
            TargetPath = path,
            Kind = TileKind.App,
            IconSourcePath = path,
        };
        DialogResult = true;
        Close();
    }

    private void NativeGameTab_Click(object sender, RoutedEventArgs e) => IsEmulatedGame = false;

    private void EmulatedGameTab_Click(object sender, RoutedEventArgs e) => IsEmulatedGame = true;

    private void BrowseGamePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Choose a game",
        };

        if (dialog.ShowDialog(this) == true)
        {
            GamePath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(GameTitle))
            {
                GameTitle = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var path = GamePath.Trim();
        if (path.Length == 0) return;

        var name = string.IsNullOrWhiteSpace(GameTitle)
            ? System.IO.Path.GetFileNameWithoutExtension(path)
            : GameTitle.Trim();

        ResultTile = new Tile
        {
            Name = name,
            TargetPath = path,
            Kind = TileKind.Game,
            IconSourcePath = path,
            PauseWallpaperWhileRunning = PauseWallpaperWhileRunning,
        };
        DialogResult = true;
        Close();
    }

    private void BrowseRomPath_Click(object sender, RoutedEventArgs e)
    {
        // .m3u (a multi-disc playlist, listing each disc's real image file) is always offered
        // alongside the platform's own extensions, not just for platforms that happen to list
        // it - it's a RetroArch-wide convention, not an emulator-core-specific format, and is
        // what should be picked for a multi-disc game rather than browsing to one disc file.
        var extensions = (SelectedPlatform?.RomExtensions ?? Array.Empty<string>()).Append(".m3u").Distinct();
        var filter = $"ROM files|{string.Join(";", extensions.Select(ext => "*" + ext))}|All files (*.*)|*.*";

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            Title = "Choose a ROM",
            InitialDirectory = SelectedPlatform is not null
                ? System.IO.Path.Combine(EmulationPlatforms.RomsRootPath, SelectedPlatform.Slug)
                : EmulationPlatforms.RomsRootPath,
        };

        if (dialog.ShowDialog(this) == true)
        {
            RomPath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(EmulatedGameTitle))
            {
                EmulatedGameTitle = RomTitleCleaner.Clean(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
            }
        }
    }

    private void AddEmulatedGame_Click(object sender, RoutedEventArgs e)
    {
        var romPath = RomPath.Trim();
        if (romPath.Length == 0 || SelectedPlatform is null)
        {
            return;
        }

        if (!EmulatorConfigs.TryGetValue(SelectedPlatform.Slug, out var config) ||
            string.IsNullOrWhiteSpace(config.EmulatorPath))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(EmulatedGameTitle)
            ? RomTitleCleaner.Clean(System.IO.Path.GetFileNameWithoutExtension(romPath))
            : EmulatedGameTitle.Trim();

        ResultTile = new Tile
        {
            Name = name,
            TargetPath = config.EmulatorPath,
            Kind = TileKind.Game,
            IconSourcePath = config.EmulatorPath,
            Arguments = config.ArgumentTemplate.Replace("{rom}", romPath),
            Platform = SelectedPlatform.Slug,
            PauseWallpaperWhileRunning = PauseWallpaperWhileRunning,
        };
        DialogResult = true;
        Close();
    }

    private void AddWebsite_Click(object sender, RoutedEventArgs e)
    {
        var url = WebsiteUrl.Trim();
        if (url.Length == 0 || SelectedBrowser is null) return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        var name = string.IsNullOrWhiteSpace(WebsiteName) ? DeriveNameFromUrl(url) : WebsiteName.Trim();

        ResultTile = new Tile
        {
            Name = name,
            TargetPath = url,
            Kind = TileKind.Website,
            IconSourcePath = url,
            BrowserExecutablePath = SelectedBrowser.ExecutablePath,
        };
        DialogResult = true;
        Close();
    }

    private static string DeriveNameFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }

        return url;
    }
}
