using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GridShell.Core.Interop;
using GridShell.Core.Settings;
using GridShell.Core.Tiles;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell;

public enum GridTab
{
    Apps,
    Web,
    Games,
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly (string Name, string Url)[] BuiltInWebsites =
    [
        ("YouTube", "https://www.youtube.com/"),
        ("Netflix", "https://www.netflix.com/"),
        ("Prime Video", "https://www.primevideo.com/"),
        ("Disney+", "https://www.disneyplus.com/"),
        ("Hulu", "https://www.hulu.com/"),
        ("Max", "https://www.max.com/"),
        ("Peacock", "https://www.peacocktv.com/"),
        ("Paramount+", "https://www.paramountplus.com/"),
        ("Apple TV+", "https://tv.apple.com/"),
        ("Twitch", "https://www.twitch.tv/"),
        ("Crunchyroll", "https://www.crunchyroll.com/"),
        ("YouTube Music", "https://music.youtube.com/"),
        ("Spotify", "https://open.spotify.com/"),
        ("Apple Music", "https://music.apple.com/"),
        ("Amazon Music", "https://music.amazon.com/"),
        ("Pandora", "https://www.pandora.com/"),
        ("SoundCloud", "https://soundcloud.com/"),
        ("Tidal", "https://tidal.com/"),
        ("Poki", "https://poki.com/"),
        ("CrazyGames", "https://www.crazygames.com/"),
        ("Newgrounds", "https://www.newgrounds.com/"),
        ("itch.io", "https://itch.io/"),
    ];

    private readonly List<Tile> _allTiles = new();
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private readonly ControllerInput _controller = new();
    private readonly List<Tile> _builtInWebsiteTiles = new();
    private Tile? _defaultBrowserShortcut;
    private bool _builtInWebsiteIconsLoaded;

    // The keyboard's trigger (Y/Triangle) is global, handled in the always-running taskbar
    // process — this instance is just where it actually lives (already pre-warmed, themed),
    // toggled via OpenKeyboardMessage from across processes.
    private readonly OnScreenKeyboardWindow _osk = new();

    private HWND _previousForegroundWindow;
    private string _searchText = "";
    private GridTab _activeTab = GridTab.Apps;

    private string? _steamExePath;
    private string? _heroicExePath;
    private string? _itchExePath;

    // Same reasoning as TaskbarWindow's identical timer: GetSystemMetrics/the work area can
    // still be mid-transition for a brief moment right when WM_DISPLAYCHANGE/WM_DPICHANGED
    // first arrives — re-applying again shortly after (once the OS has actually settled) catches
    // anything the immediate call read a stale/transitional value for.
    private readonly System.Windows.Threading.DispatcherTimer _displayChangeSettleTimer =
        new() { Interval = TimeSpan.FromMilliseconds(600) };

    public ObservableCollection<Tile> Tiles { get; } = new();

    /// <summary>Games tab only: the left pane's platform list - "PC" first (native, non-emulated
    /// games), then every other platform that currently has at least one tile, alphabetically.</summary>
    public ObservableCollection<string> Platforms { get; } = new();

    /// <summary>Games tab only: the right pane's box-art grid, filtered to SelectedPlatform.</summary>
    public ObservableCollection<Tile> GamesForSelectedPlatform { get; } = new();

    private string? _selectedPlatform;
    private bool _isCompactLayout;
    private bool _isClosing;
    private double _mainContentTopOffset = 72;

    public GridLength PlatformPaneWidth { get; private set; } = new(200);
    public int GameGridColumns { get; private set; } = 3;
    public Thickness MainContentPadding => new(24, _mainContentTopOffset, 24, 24);
    public Thickness MainContentMargin => new(24, _mainContentTopOffset, 24, 24);
    public bool AreLauncherShortcutsVisible => !_isCompactLayout;

    public string? SelectedPlatform
    {
        get => _selectedPlatform;
        set
        {
            if (_selectedPlatform == value)
            {
                return;
            }

            _selectedPlatform = value;
            OnPropertyChanged();
            RefreshGamesForSelectedPlatform();
        }
    }

    public string SearchText
    {
        get => _searchText;
        private set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public GridTab ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (_activeTab == value)
            {
                return;
            }

            _activeTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAppsTabActive));
            OnPropertyChanged(nameof(IsWebTabActive));
            OnPropertyChanged(nameof(IsGamesTabActive));
            OnPropertyChanged(nameof(IsAppsOrWebTabActive));
            ApplyFilter();
        }
    }

    public bool IsAppsTabActive => ActiveTab == GridTab.Apps;
    public bool IsWebTabActive => ActiveTab == GridTab.Web;
    public bool IsGamesTabActive => ActiveTab == GridTab.Games;

    /// <summary>Apps and Web both still use the single flat TileList; only Games gets the
    /// dual-pane platform/box-art layout, so TileList's own Visibility is keyed off this
    /// rather than two separate bindings.</summary>
    public bool IsAppsOrWebTabActive => !IsGamesTabActive;

    private TileKind ActiveKind => ActiveTab switch
    {
        GridTab.Web => TileKind.Website,
        GridTab.Games => TileKind.Game,
        _ => TileKind.App,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        ApplyScreenGeometry();

        _displayChangeSettleTimer.Tick += (_, _) =>
        {
            _displayChangeSettleTimer.Stop();
            ApplyScreenGeometryNative();
        };

        foreach (var (name, url) in BuiltInWebsites)
        {
            _builtInWebsiteTiles.Add(new Tile
            {
                Name = name,
                TargetPath = url,
                Kind = TileKind.Website,
                IconSourcePath = url,
                IsBuiltInWebsite = true,
            });
        }

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        _controller.ActionTriggered += OnControllerAction;
    }

    // The real taskbar is kept hidden for GridShell's whole lifetime (handled by the
    // always-running taskbar process, not here) — but it still reserves screen space while
    // hidden, so size to the full screen height (not just WorkArea) to avoid a gap showing
    // the desktop wallpaper where it used to sit. Keep WorkArea's Left offset so GridShell's
    // own sidebar (a real AppBar reservation) is still respected. Only used for the window's
    // very first (pre-HWND) sizing — ApplyScreenGeometryNative() takes over once the HWND
    // exists, since WPF properties are the only option before that.
    private void ApplyScreenGeometry()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = 0;
        Width = workArea.Width;
        Height = SystemParameters.PrimaryScreenHeight;
    }

    /// <summary>Same geometry as ApplyScreenGeometry(), but read directly from the OS in real
    /// physical pixels and applied via SetWindowPos — unlike SystemParameters.WorkArea (WPF's
    /// own cached, DPI-scaled copy), this can't be stale relative to the WM_DISPLAYCHANGE/
    /// WM_DPICHANGED message being handled, and works correctly even while the window is
    /// Hidden, which is when this mostly runs (pre-warmed, shown only on demand).</summary>
    private void ApplyScreenGeometryNative()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var workArea = AppBarManager.GetWorkArea();
        var screenWidth = GetSystemMetrics(SystemMetric.SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SystemMetric.SM_CYSCREEN);

        SetWindowPos(hwnd, HWND.NULL, workArea.left, 0, screenWidth - workArea.left, screenHeight,
            SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    private void OnControllerAction(ControllerAction action)
    {
        // Drives the existing keyboard handling (arrow-key navigation, Enter to launch, Escape
        // to back out) rather than reimplementing WrapPanel-aware grid navigation separately.
        switch (action)
        {
            case ControllerAction.Up: SyntheticKeyboard.PressKey(VK.VK_UP); break;
            case ControllerAction.Down: SyntheticKeyboard.PressKey(VK.VK_DOWN); break;
            case ControllerAction.Left: SyntheticKeyboard.PressKey(VK.VK_LEFT); break;
            case ControllerAction.Right: SyntheticKeyboard.PressKey(VK.VK_RIGHT); break;
            case ControllerAction.Accept: SyntheticKeyboard.PressKey(VK.VK_RETURN); break;
            case ControllerAction.Back: SyntheticKeyboard.PressKey(VK.VK_ESCAPE); break;
            case ControllerAction.TabPrevious: CycleTab(-1); break;
            case ControllerAction.TabNext: CycleTab(1); break;
        }
    }

    private void CycleTab(int direction)
    {
        var tabs = new[] { GridTab.Apps, GridTab.Web, GridTab.Games };
        var currentIndex = Array.IndexOf(tabs, ActiveTab);
        var nextIndex = (currentIndex + direction + tabs.Length) % tabs.Length;
        ActiveTab = tabs[nextIndex];
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        // The constructor's ApplyScreenGeometry() (WPF Left/Top/Width/Height, the only option
        // before the HWND exists) may already be stale by the time the HWND is actually created —
        // re-apply via the authoritative native path right away.
        ApplyScreenGeometryNative();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GridToggleMessage.Id)
        {
            ToggleVisibility();
            handled = true;
        }
        else if (msg == HideGridMessage.Id)
        {
            // Don't restore _previousForegroundWindow here — the taskbar already set foreground
            // to whatever window the user just selected, and restoring our own "previous" one
            // (whatever was active before the grid opened) would steal focus right back away
            // from it.
            if (IsVisible)
            {
                HideGrid(restorePreviousFocus: false);
            }

            handled = true;
        }
        else if (msg == ShowGridMessage.Id)
        {
            if (!IsVisible)
            {
                ShowGrid();
            }

            handled = true;
        }
        else if (msg == OpenKeyboardMessage.Id)
        {
            _osk.OpenIfClosed();
            handled = true;
        }
        else if (msg == OpenExplorerMessage.Id)
        {
            new FileExplorerWindow(ExplorerHandoff.ReadAndClear()).Show();
            handled = true;
        }
        else if (AltF4Blocker.IsSysCommandClose(msg, wParam))
        {
            handled = true;
        }
        else if (msg == (int)WindowMessage.WM_DISPLAYCHANGE || msg == (int)WindowMessage.WM_DPICHANGED)
        {
            // Immediately, for the common case, and again shortly after in case the OS was
            // still mid-transition the first time (confirmed empirically: a value read right on
            // the message was sometimes stale, the same read moments later was always correct).
            ApplyScreenGeometryNative();
            _displayChangeSettleTimer.Stop();
            _displayChangeSettleTimer.Start();
        }

        return IntPtr.Zero;
    }

    /// <summary>Shows the pre-warmed, already-running grid in place — this is what makes
    /// toggling instant, since it's not a fresh process launch.</summary>
    public void ShowGrid()
    {
        if (IsVisible)
        {
            return;
        }

        // Re-applying right before showing covers any display change that happened since the
        // window was last positioned, regardless of whether WM_DISPLAYCHANGE/WM_DPICHANGED fired
        // while it still had no HWND to receive them on.
        ApplyScreenGeometryNative();

        _previousForegroundWindow = GetForegroundWindow();

        var slideDistance = GetGridSlideDistance();
        ModalPanelSlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ScrimBorder.BeginAnimation(UIElement.OpacityProperty, null);
        ModalPanelSlideTransform.X = -slideDistance;
        ScrimBorder.Opacity = 0;

        Show();
        Activate();

        // Topmost="True" alone doesn't guarantee ordering against OTHER topmost windows (the
        // on-screen keyboard is also topmost, and should stay above this specifically) —
        // re-asserting HWND_TOPMOST here keeps the grid reliably above ordinary windows every
        // time it's shown. If the keyboard happens to already be open (e.g. the grid gets
        // toggled while it's up), push it back above the grid immediately after, so this never
        // ends up on top of it.
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE);
        _osk.BringAboveOthers();

        ModalPanelSlideTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(-slideDistance, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        ScrimBorder.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 0.45, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

        FocusActiveList();
        _controller.Start();
        // Re-checks every time the grid opens, not just once at startup - confirmed live: a
        // storefront installed (via BrowserInstaller, see StoreWindow) while GridShell was
        // already running stayed "not installed" on its quick-launch button until GridShell
        // itself restarted, since this only ran once before.
        DetectGameLaunchers();
    }

    private void HideGrid(bool restorePreviousFocus = true)
    {
        if (!IsVisible || _isClosing)
        {
            return;
        }

        _controller.Stop();
        _isClosing = true;

        var slideDistance = GetGridSlideDistance();
        var slideOut = new DoubleAnimation(ModalPanelSlideTransform.X, -slideDistance, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        slideOut.Completed += (_, _) =>
        {
            ModalPanelSlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ModalPanelSlideTransform.X = -slideDistance;
            ScrimBorder.BeginAnimation(UIElement.OpacityProperty, null);
            ScrimBorder.Opacity = 0;
            Hide();
            _isClosing = false;

            if (restorePreviousFocus && !_previousForegroundWindow.IsNull)
            {
                SetForegroundWindow(_previousForegroundWindow);
            }
        };

        ModalPanelSlideTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        ScrimBorder.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(ScrimBorder.Opacity, 0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
    }

    private double GetGridSlideDistance() =>
        Math.Max(RootGrid.ActualWidth, Math.Max(Width, SystemParameters.PrimaryScreenWidth));

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            HideGrid();
        }
        else
        {
            ShowGrid();
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var tile in TileStore.Load())
        {
            _allTiles.Add(tile);
            _ = IconLoader.LoadIconAsync(tile, _settings);
        }

        ApplyFilter();
        TileList.Focus();
        ApplySettings();
        DetectGameLaunchers();
    }

    private void DetectGameLaunchers()
    {
        _ = Task.Run(() =>
        {
            _steamExePath = GameLauncherDetector.FindExe(GameLauncher.Steam);
            _heroicExePath = GameLauncherDetector.FindExe(GameLauncher.Heroic);
            _itchExePath = GameLauncherDetector.FindExe(GameLauncher.Itch);
        });
    }

    private void SteamButton_Click(object sender, RoutedEventArgs e) => LaunchGameLauncher(_steamExePath);
    private void HeroicButton_Click(object sender, RoutedEventArgs e) => LaunchGameLauncher(_heroicExePath);
    private void ItchButton_Click(object sender, RoutedEventArgs e) => LaunchGameLauncher(_itchExePath);

    private void LaunchGameLauncher(string? exePath)
    {
        if (exePath is null)
        {
            new ToastWindow("Not currently installed.", TimeSpan.FromSeconds(5)).Show();
            return;
        }

        var before = new HashSet<HWND>(WindowEnumerator.EnumerateEligibleWindows());
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
        HideGrid(restorePreviousFocus: false);

        // None of Steam/Heroic/itch remember "always start maximized" on their own - same
        // "wait for its window, then force it" pattern TileLauncher already uses for the same
        // reason (a freshly launched process doesn't reliably get focus, let alone a specific
        // window state, on its own).
        _ = Task.Run(() =>
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                Thread.Sleep(100);
                var newWindow = WindowEnumerator.EnumerateEligibleWindows().FirstOrDefault(h => !before.Contains(h));
                if (!newWindow.IsNull)
                {
                    WindowEnumerator.ActivateMaximized(newWindow);
                    return;
                }
            }
        });
    }

    private void ApplySettings()
    {
        var (w, h) = _settings.TileSize switch
        {
            TileSize.Small => (100.0, 80.0),
            TileSize.Large => (180.0, 140.0),
            _ => (140.0, 110.0),
        };
        Resources["TileWidth"] = w;
        Resources["TileHeight"] = h;

        // Initial size until the Games grid is measured; GamesGrid_SizeChanged scales covers to
        // the available display area while preserving this setting's relative size.
        Resources["GameTileWidth"] = w;
        Resources["GameTileHeight"] = w * 1.4;

        // Exactly 1 row tall at the configured tile size; each cover has an 8px margin on every side.
        Resources["GamesGridVisibleHeight"] = w * 1.4 + 16;

        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.AccentColor)!;
            ThemePalette.ApplyAccent(Application.Current.Resources, color);

            var selectionBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                RelativeTransform = new RotateTransform(0, 0.5, 0.5),
            };
            selectionBrush.GradientStops.Add(new GradientStop(ThemePalette.DarkenAccent(color, 0.72), 0));
            selectionBrush.GradientStops.Add(new GradientStop(ThemePalette.DarkenAccent(color, 0.48), 0.5));
            selectionBrush.GradientStops.Add(new GradientStop(ThemePalette.DarkenAccent(color, 0.72), 1));
            Resources["TileSelectionBorderBrush"] = selectionBrush;

            var modalBorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            };
            modalBorderBrush.GradientStops.Add(new GradientStop(ThemePalette.DarkenAccent(color, 0.72), 0));
            modalBorderBrush.GradientStops.Add(new GradientStop(ThemePalette.DarkenAccent(color, 0.48), 1));
            Resources["ModalBorderBrush"] = modalBorderBrush;
        }
        catch (FormatException) { }

        // Same app-level pattern as AccentBrush - every window references
        // {DynamicResource AppFontFamily} instead of a hardcoded font, so switching this one
        // resource updates the whole process live.
        Application.Current.Resources["AppFontFamily"] = UiFontResolver.Resolve(_settings);

        if (_defaultBrowserShortcut is not null)
        {
            _defaultBrowserShortcut.Icon = CreateBrowserGlyph();
        }

        // Kiosk Mode's only unlock path is Settings (PIN-gated there) - these two surfaces get
        // disabled rather than PIN-prompted here, so there's exactly one place a kid could even
        // attempt to guess a PIN, not several.
        AddTileButton.IsEnabled = !_settings.KioskModeEnabled;
        PowerButton.IsEnabled = !_settings.KioskModeEnabled;
    }

    private void GamesGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (GamesGrid.Parent is not Grid gamesPane || GamesGrid.ActualWidth <= 0 || gamesPane.ActualHeight <= 0)
        {
            return;
        }

        var platformPaneWidth = Math.Clamp(gamesPane.ActualWidth * 0.28, 72, 200);
        var platformPane = new GridLength(platformPaneWidth);
        if (PlatformPaneWidth != platformPane)
        {
            PlatformPaneWidth = platformPane;
            OnPropertyChanged(nameof(PlatformPaneWidth));
        }

        var (baseTileWidth, _) = _settings.TileSize switch
        {
            TileSize.Small => (100.0, 80.0),
            TileSize.Large => (180.0, 140.0),
            _ => (140.0, 110.0),
        };
        var columns = Math.Clamp((int)(GamesGrid.ActualWidth / (baseTileWidth + 16)), 1, 3);
        if (GameGridColumns != columns)
        {
            GameGridColumns = columns;
            OnPropertyChanged(nameof(GameGridColumns));
        }

        var widthLimit = (GamesGrid.ActualWidth / columns) - 16;
        var heightLimit = (gamesPane.ActualHeight - 16) / 1.4;
        var maxTileWidth = Math.Min(widthLimit, heightLimit);
        var tileWidth = Math.Min(maxTileWidth, baseTileWidth);
        if (tileWidth <= 0)
        {
            return;
        }

        var tileHeight = tileWidth * 1.4;
        Resources["GameTileWidth"] = tileWidth;
        Resources["GameTileHeight"] = tileHeight;
        Resources["GamesGridVisibleHeight"] = tileHeight + 16;
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var isCompact = RootGrid.ActualWidth < 620;
        if (_isCompactLayout != isCompact)
        {
            _isCompactLayout = isCompact;
            OnPropertyChanged(nameof(AreLauncherShortcutsVisible));
        }
    }

    private void HeaderPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var topOffset = Math.Max(72, HeaderPanel.ActualHeight + 36);
        if (Math.Abs(_mainContentTopOffset - topOffset) < 0.5)
        {
            return;
        }

        _mainContentTopOffset = topOffset;
        OnPropertyChanged(nameof(MainContentPadding));
        OnPropertyChanged(nameof(MainContentMargin));
    }

    public void OpenSettings()
    {
        var dialog = new SettingsWindow(
            _settings,
            onSettingsChanged: () =>
            {
                AppSettingsStore.Save(_settings);
                ApplySettings();
            })
        {
            Owner = this,
        };

        dialog.ShowDialog();
    }

    public void OpenStore()
    {
        var dialog = new StoreWindow(
            _allTiles,
            _settings,
            onTilesChanged: () =>
            {
                TileStore.Save(_allTiles);
                ApplyFilter();
            })
        {
            Owner = this,
        };

        dialog.Show();
    }

    public void OpenGameManager()
    {
        var dialog = new GameManagerWindow(
            _allTiles,
            _settings,
            onTilesChanged: () =>
            {
                TileStore.Save(_allTiles);
                ApplyFilter();
            },
            onSettingsChanged: () =>
            {
                AppSettingsStore.Save(_settings);
                ApplySettings();
            })
        {
            Owner = this,
        };

        dialog.ShowDialog();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _displayChangeSettleTimer.Stop();
        _controller.Dispose();
        _osk.Close();

        // This window is normally just hidden (HideGrid), never closed, for every ordinary
        // toggle — a real Close() only happens via "Exit GridShell" in Settings, so it means
        // the process should actually end. ShutdownMode is OnExplicitShutdown, so make that
        // explicit rather than relying on window-count bookkeeping.
        Application.Current.Shutdown();
    }

    private void ApplyFilter()
    {
        var matches = _allTiles.Where(t => t.Kind == ActiveKind);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            matches = matches.Where(t => t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var matchList = matches.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        if (ActiveTab == GridTab.Games)
        {
            RefreshPlatforms(matchList);
        }
        else
        {
            Tiles.Clear();
            if (ActiveTab == GridTab.Apps && ShouldShowDefaultBrowserShortcut())
            {
                Tiles.Add(GetDefaultBrowserShortcut());
            }

            if (ActiveTab == GridTab.Web)
            {
                if (!_builtInWebsiteIconsLoaded)
                {
                    _builtInWebsiteIconsLoaded = true;
                    foreach (var website in _builtInWebsiteTiles)
                    {
                        _ = IconLoader.LoadIconAsync(website, _settings);
                    }
                }

                foreach (var website in _builtInWebsiteTiles.Where(website =>
                             string.IsNullOrWhiteSpace(SearchText)
                             || website.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
                {
                    Tiles.Add(website);
                }
            }

            foreach (var tile in matchList)
            {
                Tiles.Add(tile);
            }

            TileList.SelectedIndex = Tiles.Count > 0 ? 0 : -1;
        }

        // Filtering rebuilds the virtualized containers, which can silently drop keyboard
        // focus out of the list. Re-assert it so mouse and keyboard always share the same
        // selection/focus state instead of drifting apart.
        FocusActiveList();
    }

    private bool ShouldShowDefaultBrowserShortcut()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var executableName = System.IO.Path.GetFileNameWithoutExtension(_settings.DefaultBrowserExecutablePath ?? "");
        return "Browser".Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || executableName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private Tile GetDefaultBrowserShortcut()
    {
        if (_defaultBrowserShortcut is null)
        {
            _defaultBrowserShortcut = new Tile
            {
                Name = "Browser",
                TargetPath = "",
                Kind = TileKind.App,
                IconSourcePath = "",
                Icon = CreateBrowserGlyph(),
            };
        }

        return _defaultBrowserShortcut;
    }

    private static System.Windows.Media.ImageSource CreateBrowserGlyph() =>
        GlyphIcon.Render(
            '\ue80b',
            Brushes.Transparent,
            Application.Current.Resources["PrimaryForegroundBrush"] as Brush ?? Brushes.White);

    /// <summary>Games tab's left pane: "PC" first (native, non-emulated games), then every
    /// other platform with at least one matching tile, alphabetically - rebuilt from the
    /// already kind/search-filtered list every time ApplyFilter runs, same as Tiles is for
    /// Apps/Web. Keeps the previously selected platform selected when it's still present
    /// (e.g. after adding another game to it), rather than always resetting to the top.</summary>
    private void RefreshPlatforms(List<Tile> matchList)
    {
        var previousSelection = SelectedPlatform;

        var platforms = matchList.Select(t => t.PlatformGroupName).Distinct()
            .OrderBy(name => name == "PC" ? 0 : 1)
            .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Platforms.Clear();
        foreach (var name in platforms)
        {
            Platforms.Add(name);
        }

        var nextSelection = previousSelection is not null && platforms.Contains(previousSelection)
            ? previousSelection
            : platforms.FirstOrDefault();
        if (nextSelection == SelectedPlatform)
        {
            // SelectedPlatform's own setter already calls this, but a same-value assignment
            // (same platform still selected) short-circuits before that - the underlying game
            // list still needs rebuilding since matchList itself just changed.
            RefreshGamesForSelectedPlatform();
        }
        else
        {
            SelectedPlatform = nextSelection;
        }
    }

    private void RefreshGamesForSelectedPlatform()
    {
        GamesForSelectedPlatform.Clear();
        if (SelectedPlatform is null)
        {
            return;
        }

        foreach (var tile in _allTiles
                     .Where(t => t.Kind == TileKind.Game && t.PlatformGroupName == SelectedPlatform)
                     .Where(t => string.IsNullOrWhiteSpace(SearchText) || t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            GamesForSelectedPlatform.Add(tile);
        }

        GamesGrid.SelectedIndex = GamesForSelectedPlatform.Count > 0 ? 0 : -1;
    }

    private TranslateTransform? _platformMarqueeTransform;

    private void PlatformList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlatformList.SelectedItem is string platform)
        {
            SelectedPlatform = platform;
        }

        // Dispatcher.BeginInvoke at Loaded priority - Platforms may have just been
        // repopulated (RefreshPlatforms) this same tick, so the newly selected item's
        // container isn't realized/measured yet at the moment this event fires.
        Dispatcher.BeginInvoke(new Action(UpdatePlatformNameMarquee), DispatcherPriority.Loaded);
    }

    /// <summary>Only the selected platform's own name ever scrolls - every other entry just
    /// shows a static ellipsis (see PlatformList's ItemTemplate) - so a name too long to fit
    /// is still fully readable without anything spilling past ClipBorder's own hard edge.</summary>
    private void UpdatePlatformNameMarquee()
    {
        if (_platformMarqueeTransform is not null)
        {
            _platformMarqueeTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _platformMarqueeTransform.X = 0;
            _platformMarqueeTransform = null;
        }

        if (PlatformList.SelectedItem is not string platform ||
            PlatformList.ItemContainerGenerator.ContainerFromItem(platform) is not ListBoxItem container)
        {
            return;
        }

        container.UpdateLayout();
        if (FindVisualChild<ContentPresenter>(container) is not { } presenter)
        {
            return;
        }

        presenter.ApplyTemplate();
        if (PlatformList.ItemTemplate.FindName("PlatformNameText", presenter) is not TextBlock textBlock ||
            PlatformList.ItemTemplate.FindName("ClipBorder", presenter) is not FrameworkElement clipBorder ||
            textBlock.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overflow = textBlock.DesiredSize.Width - clipBorder.ActualWidth;
        if (overflow <= 1)
        {
            return;
        }

        _platformMarqueeTransform = transform;
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(Math.Max(4, 4 + overflow / 20)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.12)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromPercent(0.55)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromPercent(0.75)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }

            if (FindVisualChild<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void AppsTab_Click(object sender, RoutedEventArgs e) => ActiveTab = GridTab.Apps;

    private void WebTab_Click(object sender, RoutedEventArgs e) => ActiveTab = GridTab.Web;

    private void GamesTab_Click(object sender, RoutedEventArgs e) => ActiveTab = GridTab.Games;

    private void GameManagerButton_Click(object sender, RoutedEventArgs e)
    {
        HideGrid(restorePreviousFocus: false);
        OpenGameManager();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HideGrid(restorePreviousFocus: false);
        OpenSettings();
    }

    private void ExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        // Same spirit as launching a tile - a real app window is taking over, so the grid
        // dismisses itself rather than sitting underneath it. restorePreviousFocus: false for
        // the same reason as ActivateSelected above - the window that just opened should keep
        // focus, not lose it back to whatever was active before the grid opened.
        new FileExplorerWindow().Show();
        HideGrid(restorePreviousFocus: false);
    }

    private void StoreButton_Click(object sender, RoutedEventArgs e)
    {
        OpenStore();
        HideGrid(restorePreviousFocus: false);
    }

    private void DownloadManagerButton_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).ShowDownloadManager();
        HideGrid(restorePreviousFocus: false);
    }

    private void AddTileButton_Click(object sender, RoutedEventArgs e) => OpenAddTileDialog();

    private DateTime _powerMenuClosedAt;

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.KioskModeEnabled)
        {
            return;
        }

        // Clicking the button again while the menu is open should close it (not reopen it) —
        // but WPF's own light-dismiss already closes the menu on that same click, BEFORE this
        // Click handler runs, so by the time we get here IsOpen is already false either way.
        // A short time window after Closed distinguishes "this click just dismissed it" from
        // a genuinely new, later click that should reopen it.
        if ((DateTime.UtcNow - _powerMenuClosedAt).TotalMilliseconds < 200)
        {
            return;
        }

        // Setting PlacementTarget via an ElementName binding in XAML doesn't reliably resolve —
        // a ContextMenu's content lives in its own namescope — so it's set here instead, right
        // before opening, where the button reference is unambiguous.
        PowerMenu.PlacementTarget = PowerButton;
        PowerMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        PowerMenu.IsOpen = true;
    }

    private void PowerMenu_Closed(object sender, RoutedEventArgs e) => _powerMenuClosedAt = DateTime.UtcNow;

    private void LockMenuItem_Click(object sender, RoutedEventArgs e) => PowerActions.Lock();

    private void SignOutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmPowerAction("Sign out", "This will close everything you have open and sign you out."))
        {
            PowerActions.SignOut();
        }
    }

    private void SleepMenuItem_Click(object sender, RoutedEventArgs e) => PowerActions.Sleep();

    private void RestartMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmPowerAction("Restart", "This will close everything you have open and restart the PC."))
        {
            PowerActions.Restart();
        }
    }

    private void ShutDownMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmPowerAction("Shut down", "This will close everything you have open and shut down the PC."))
        {
            PowerActions.ShutDown();
        }
    }

    private bool ConfirmPowerAction(string title, string message) =>
        ConfirmationWindow.Confirm(this, title, message);

    // WPF's default ContextMenu light-dismiss (StaysOpen=false) doesn't reliably close on an
    // outside click for a Topmost + AllowsTransparency window like this one — confirmed by
    // testing, a tile's right-click menu stayed open after clicking elsewhere on the grid.
    // This handler fires for every mouse-down landing on the window itself (a click actually
    // on the open popup is a separate HWND, so it never reaches this), closing whichever
    // tile context menu is open without otherwise touching the click.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindResource("TileContextMenu") is ContextMenu { IsOpen: true } tileMenu)
        {
            tileMenu.IsOpen = false;
        }

        if (FindResource("GameTileContextMenu") is ContextMenu { IsOpen: true } gameTileMenu)
        {
            gameTileMenu.IsOpen = false;
        }

        // Modal light-dismiss - a click landing directly on the scrim (never on the panel or
        // anything inside it, since those are separate elements under the pointer) closes the
        // grid, same as clicking outside any other modal.
        if (ReferenceEquals(e.OriginalSource, ScrimBorder))
        {
            HideGrid();
        }
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            SearchText += e.Text;
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (!string.IsNullOrEmpty(SearchText))
                {
                    SearchText = "";
                }
                else
                {
                    // Hide, not Close() — this is a long-lived pre-warmed process now; closing
                    // it would mean relaunching (and paying startup cost) on the next toggle.
                    HideGrid();
                }

                e.Handled = true;
                break;

            case Key.Back:
                if (SearchText.Length > 0)
                {
                    SearchText = SearchText[..^1];
                }

                e.Handled = true;
                break;

            case Key.Enter:
                ActivateSelected();
                e.Handled = true;
                break;

            case Key.Delete:
                RemoveTile(SelectedTile);
                e.Handled = true;
                break;
        }
    }

    private void TileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Must resolve to the tile actually under the cursor, not whatever's currently
        // selected — otherwise a double-click landing on empty grid space (past the last
        // tile, in the gutter between tiles) would launch a stale selection instead of
        // doing nothing.
        if (FindAncestorTile((DependencyObject)e.OriginalSource) is not { } tile)
        {
            return;
        }

        ActivateTile(tile);
    }

    private static Tile? FindAncestorTile(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListBoxItem { Content: Tile tile })
            {
                return tile;
            }
        }

        return null;
    }

    private void ActivateSelected()
    {
        if (SelectedTile is { } tile)
        {
            ActivateTile(tile);
        }
    }

    /// <summary>Whichever of TileList (Apps/Web) or GamesGrid (Games' dual-pane box-art list)
    /// is actually showing right now - keyboard actions (Enter/Delete) and focus restoration
    /// need to target the one the user can currently see, not always TileList.</summary>
    private Tile? SelectedTile => IsGamesTabActive ? GamesGrid.SelectedItem as Tile : TileList.SelectedItem as Tile;

    private void FocusActiveList()
    {
        if (IsGamesTabActive)
        {
            GamesGrid.Focus();
        }
        else
        {
            TileList.Focus();
        }
    }

    private void ActivateTile(Tile tile)
    {
        if (ReferenceEquals(tile, _defaultBrowserShortcut))
        {
            var browserPath = _settings.DefaultBrowserExecutablePath;
            if (string.IsNullOrWhiteSpace(browserPath))
            {
                new ToastWindow("Choose a default browser in Settings first.", TimeSpan.FromSeconds(5), isWarning: true).Show();
                return;
            }

            ActivateTile(new Tile
            {
                Name = "Browser",
                TargetPath = browserPath,
                Kind = TileKind.App,
                IconSourcePath = browserPath,
            });
            return;
        }

        if (tile.IsBuiltInWebsite)
        {
            if (string.IsNullOrWhiteSpace(_settings.DefaultBrowserExecutablePath))
            {
                new ToastWindow("Choose a default browser in Settings first.", TimeSpan.FromSeconds(5), isWarning: true).Show();
                return;
            }

            tile = new Tile
            {
                Name = tile.Name,
                TargetPath = tile.TargetPath,
                Kind = tile.Kind,
                IconSourcePath = tile.IconSourcePath,
                BrowserExecutablePath = _settings.DefaultBrowserExecutablePath,
            };
        }

        if (TileLauncher.Launch(tile))
        {
            // Same spirit as Escape — once something's actually launched, the grid's done its
            // job, same as a real Start menu dismissing itself after opening an app. Stays open
            // on a failed launch so the user can see it didn't work and try something else.
            //
            // restorePreviousFocus: false — same reasoning as HideGridMessage's handler above.
            // TileLauncher is actively forcing focus onto the thing that was just launched; the
            // slide-out animation's own focus restore (to whatever was active BEFORE the grid
            // opened) finishes a couple hundred ms later and would steal focus right back from
            // it otherwise - confirmed by real testing (the game needed a manual click after
            // launching even with TileLauncher's own fix in place).
            HideGrid(restorePreviousFocus: false);
        }
    }

    private void OpenAddTileDialog()
    {
        if (_settings.KioskModeEnabled)
        {
            return;
        }

        var dialog = new AddTileWindow
        {
            Owner = this,
            RequestedKind = ActiveTab switch
            {
                GridTab.Web => TileKind.Website,
                GridTab.Games => TileKind.Game,
                _ => TileKind.App,
            },
            DefaultBrowserExecutablePath = _settings.DefaultBrowserExecutablePath,
        };

        if (dialog.ShowDialog() == true && dialog.ResultTile is { } newTile)
        {
            _allTiles.Add(newTile);
            TileStore.Save(_allTiles);
            _ = IconLoader.LoadIconAsync(newTile, _settings);
            ActiveTab = newTile.Kind switch
            {
                TileKind.Website => GridTab.Web,
                TileKind.Game => GridTab.Games,
                _ => GridTab.Apps,
            };
            ApplyFilter();
        }

        // Returning from the dialog otherwise leaves keyboard focus nowhere in particular.
        FocusActiveList();
    }

    private void RenameTileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.KioskModeEnabled || (sender as MenuItem)?.CommandParameter is not Tile tile)
        {
            return;
        }

        var dialog = new RenameTileWindow(tile.Name) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            tile.Name = dialog.NewName;
            TileStore.Save(_allTiles);
        }

        FocusActiveList();
    }

    private void ChangeIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.KioskModeEnabled || (sender as MenuItem)?.CommandParameter is not Tile tile)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All files (*.*)|*.*",
            Title = "Choose an icon",
        };

        if (dialog.ShowDialog(this) == true)
        {
            tile.CustomIconPath = dialog.FileName;
            TileStore.Save(_allTiles);
            _ = IconLoader.LoadIconAsync(tile, _settings);
        }

        FocusActiveList();
    }

    private void RemoveTileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RemoveTile((sender as MenuItem)?.CommandParameter as Tile);
    }

    private void ScrapeCoverMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.KioskModeEnabled || (sender as MenuItem)?.CommandParameter is not Tile tile)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.IgdbClientId) || string.IsNullOrWhiteSpace(_settings.IgdbClientSecret))
        {
            new ToastWindow("Set an IGDB Client ID and Secret in Settings → Tiles → Games first.", TimeSpan.FromSeconds(5)).Show();
            return;
        }

        var dialog = new ScrapeCoverWindow(tile.Name, _settings.IgdbClientId, _settings.IgdbClientSecret) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedCoverPath is { } coverPath)
        {
            tile.CustomIconPath = coverPath;
            TileStore.Save(_allTiles);
            _ = IconLoader.LoadIconAsync(tile, _settings);
        }

        FocusActiveList();
    }

    private void RemoveTile(Tile? tile)
    {
        if (tile is null || tile.IsBuiltInWebsite || ReferenceEquals(tile, _defaultBrowserShortcut) || _settings.KioskModeEnabled)
        {
            return;
        }

        if (!FileOperations.TryDeleteGameFile(tile, this))
        {
            return;
        }

        _allTiles.Remove(tile);
        TileStore.Save(_allTiles);
        ApplyFilter();
    }
}
