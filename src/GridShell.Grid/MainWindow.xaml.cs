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
    private readonly List<Tile> _allTiles = new();
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private readonly ControllerInput _controller = new();

    // The keyboard's trigger (Y/Triangle) is global, handled in the always-running taskbar
    // process — this instance is just where it actually lives (already pre-warmed, themed),
    // toggled via OpenKeyboardMessage from across processes.
    private readonly OnScreenKeyboardWindow _osk = new();

    private HWND _previousForegroundWindow;
    private string _searchText = "";
    private GridTab _activeTab = GridTab.Apps;

    // Same reasoning as TaskbarWindow's identical timer: GetSystemMetrics/the work area can
    // still be mid-transition for a brief moment right when WM_DISPLAYCHANGE/WM_DPICHANGED
    // first arrives — re-applying again shortly after (once the OS has actually settled) catches
    // anything the immediate call read a stale/transitional value for.
    private readonly System.Windows.Threading.DispatcherTimer _displayChangeSettleTimer =
        new() { Interval = TimeSpan.FromMilliseconds(600) };

    public ObservableCollection<Tile> Tiles { get; } = new();

    public string SearchDisplay => string.IsNullOrEmpty(SearchText) ? "Type to search" : SearchText;

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
            OnPropertyChanged(nameof(SearchDisplay));
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
            ApplyFilter();
        }
    }

    public bool IsAppsTabActive => ActiveTab == GridTab.Apps;
    public bool IsWebTabActive => ActiveTab == GridTab.Web;
    public bool IsGamesTabActive => ActiveTab == GridTab.Games;

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
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(260);

    public void ShowGrid()
    {
        // Re-applying right before showing covers any display change that happened since the
        // window was last positioned, regardless of whether WM_DISPLAYCHANGE/WM_DPICHANGED fired
        // while it still had no HWND to receive them on.
        ApplyScreenGeometryNative();

        _previousForegroundWindow = GetForegroundWindow();

        // Starts fully off-screen to the left before Show() so there's no flash of the
        // fully-in-place grid before the slide-in animation takes over.
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SlideTransform.X = -SystemParameters.PrimaryScreenWidth;
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

        TileList.Focus();
        _controller.Start();

        var slideIn = new DoubleAnimation(-SystemParameters.PrimaryScreenWidth, 0, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
    }

    private void HideGrid(bool restorePreviousFocus = true)
    {
        _controller.Stop();

        // Slides back out before actually hiding — Hide() is instant, so without this the
        // window would just vanish rather than appearing to leave.
        var slideOut = new DoubleAnimation(0, -SystemParameters.PrimaryScreenWidth, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        slideOut.Completed += (_, _) =>
        {
            Hide();
            if (restorePreviousFocus && !_previousForegroundWindow.IsNull)
            {
                SetForegroundWindow(_previousForegroundWindow);
            }
        };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
    }

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

        // Portrait box-art ratio (~1:1.4, close to a standard game cover) at the same width as
        // the other tile kinds, so the Small/Medium/Large setting still scales Games sensibly.
        Resources["GameTileWidth"] = w;
        Resources["GameTileHeight"] = w * 1.4;

        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.AccentColor)!;
            // Application-level (not this window's own Resources) so other windows — the
            // on-screen keyboard, and anything else added later — can use it too.
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);
        }
        catch (FormatException) { }

        // Same app-level pattern as AccentBrush - every window references
        // {DynamicResource AppFontFamily} instead of a hardcoded font, so switching this one
        // resource updates the whole process live.
        Application.Current.Resources["AppFontFamily"] = UiFontResolver.Resolve(_settings);

        // Kiosk Mode's only unlock path is Settings (PIN-gated there) - these two surfaces get
        // disabled rather than PIN-prompted here, so there's exactly one place a kid could even
        // attempt to guess a PIN, not several.
        AddTileButton.IsEnabled = !_settings.KioskModeEnabled;
        PowerButton.IsEnabled = !_settings.KioskModeEnabled;
    }

    public void OpenSettings()
    {
        var dialog = new SettingsWindow(
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

        matches = matches.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);

        Tiles.Clear();
        foreach (var tile in matches)
        {
            Tiles.Add(tile);
        }

        // Only the Games tab groups by platform - Apps/Web never asked for it, and a single
        // "PC" header over every app/website tile would just be noise.
        var groupedView = (System.Windows.Data.CollectionViewSource)Resources["GroupedTiles"];
        groupedView.GroupDescriptions.Clear();
        groupedView.SortDescriptions.Clear();
        if (ActiveTab == GridTab.Games)
        {
            groupedView.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(Tile.PlatformGroupName), System.ComponentModel.ListSortDirection.Ascending));
            groupedView.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(Tile.Name), System.ComponentModel.ListSortDirection.Ascending));
            groupedView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(Tile.PlatformGroupName)));
        }

        TileList.SelectedIndex = Tiles.Count > 0 ? 0 : -1;

        // Filtering rebuilds the virtualized containers, which can silently drop keyboard
        // focus out of the list. Re-assert it so mouse and keyboard always share the same
        // selection/focus state instead of drifting apart.
        TileList.Focus();
    }

    private void AppsTab_Click(object sender, RoutedEventArgs e) => ActiveTab = GridTab.Apps;

    private void WebTab_Click(object sender, RoutedEventArgs e) => ActiveTab = GridTab.Web;

    private void GamesTab_Click(object sender, RoutedEventArgs e) => ActiveTab = GridTab.Games;

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void ControllerManagerButton_Click(object sender, RoutedEventArgs e) =>
        new ControllerManagerWindow { Owner = this }.ShowDialog();

    private void ExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        // Same spirit as launching a tile - a real app window is taking over, so the grid
        // dismisses itself rather than sitting underneath it. restorePreviousFocus: false for
        // the same reason as ActivateSelected above - the window that just opened should keep
        // focus, not lose it back to whatever was active before the grid opened.
        new FileExplorerWindow().Show();
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
                RemoveTile(TileList.SelectedItem as Tile);
                e.Handled = true;
                break;
        }
    }

    private void TileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ActivateSelected();
    }

    private void ActivateSelected()
    {
        if (TileList.SelectedItem is not Tile tile)
        {
            return;
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
            EmulatorConfigs = _settings.EmulatorConfigs,
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
        TileList.Focus();
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

        TileList.Focus();
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

        TileList.Focus();
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
            System.Windows.MessageBox.Show(this, "Set an IGDB Client ID and Secret in Settings → Tiles → Games first.",
                "Scrape Cover Art", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ScrapeCoverWindow(tile.Name, _settings.IgdbClientId, _settings.IgdbClientSecret) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedCoverPath is { } coverPath)
        {
            tile.CustomIconPath = coverPath;
            TileStore.Save(_allTiles);
            _ = IconLoader.LoadIconAsync(tile, _settings);
        }

        TileList.Focus();
    }

    private void RemoveTile(Tile? tile)
    {
        if (tile is null || _settings.KioskModeEnabled)
        {
            return;
        }

        _allTiles.Remove(tile);
        TileStore.Save(_allTiles);
        ApplyFilter();
    }
}
