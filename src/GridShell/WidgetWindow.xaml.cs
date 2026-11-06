using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GridShell.Core;
using GridShell.Core.Interop;
using GridShell.Core.Widgets;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell;

/// <summary>One floating desktop widget - content depends on widget.Kind (see WidgetKind), all
/// rendered with plain WPF controls (Image/MediaElement/TextBlock/ListBox), deliberately never
/// WebView2 - confirmed via Microsoft's own open WebView2 feedback issue
/// (MicrosoftEdge/WebView2Feedback#1004) that a WebView2 control always consumes clicks itself
/// with no supported way to make it click-through or non-activating (it's a real windowed
/// control with its own native child HWND, which can grab keyboard focus on click and activate
/// the parent window regardless of WS_EX_NOACTIVATE/WS_EX_TRANSPARENT). Plain WPF visuals have
/// no child HWND to intercept clicks at all. Never activatable (NeverActivate); click-through
/// (MakeClickThrough) for every kind except IconGrid, which needs to receive the double-click
/// that launches an item.
///
/// Z-order: placed at HWND_TOP (not HWND_TOPMOST) on creation - confirmed via Microsoft's own
/// SetWindowPos docs that SetWindowPos(A, B) places A BEHIND B, the opposite of an earlier,
/// unverified assumption in this code. HWND_TOP puts the widget above the wallpaper (and
/// whatever else happens to be open at that instant) without ever claiming true topmost
/// status, so the very next window the user activates - any ordinary app window - is raised
/// above it again, same as any normal, non-topmost window. Re-asserted on every Activated
/// (belt-and-suspenders for the IconGrid kind, which deliberately isn't click-through and so
/// can legitimately receive focus/activation from a real click) so this window can never climb
/// above where it belongs just because something activated it.</summary>
public partial class WidgetWindow : Window, INotifyPropertyChanged
{
    private readonly DesktopWidget _widget;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _statsTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public bool IsClockKind => _widget.Kind == WidgetKind.Clock;
    public bool IsIconGridKind => _widget.Kind == WidgetKind.IconGrid;
    public bool IsSystemStatsKind => _widget.Kind == WidgetKind.SystemStats;

    public bool IsImageMedia => _widget.Kind == WidgetKind.Media && _widget.MediaPath is { } p && MediaWrapperHtml.IsImage(p);
    public bool IsVideoMedia => _widget.Kind == WidgetKind.Media && _widget.MediaPath is { } p && MediaWrapperHtml.IsVideo(p);

    private BitmapImage? _imageSource;
    public BitmapImage? ImageSource
    {
        get => _imageSource;
        set { _imageSource = value; OnPropertyChanged(); }
    }

    private string _clockTimeText = "";
    public string ClockTimeText { get => _clockTimeText; set { _clockTimeText = value; OnPropertyChanged(); } }

    private string _clockDateText = "";
    public string ClockDateText { get => _clockDateText; set { _clockDateText = value; OnPropertyChanged(); } }

    private string _cpuText = "CPU: --%";
    public string CpuText { get => _cpuText; set { _cpuText = value; OnPropertyChanged(); } }

    private string _ramText = "RAM: --%";
    public string RamText { get => _ramText; set { _ramText = value; OnPropertyChanged(); } }

    public ObservableCollection<DesktopIconEntry> IconEntries { get; } = new();

    public WidgetWindow(DesktopWidget widget)
    {
        _widget = widget;

        InitializeComponent();
        DataContext = this;

        // Placeholder (WPF logical/DPI-scaled props) until SourceInitialized sets the real
        // physical-pixel bounds and z-order below.
        Left = widget.X;
        Top = widget.Y;
        Width = widget.Width;
        Height = widget.Height;

        this.NeverActivate();

        // Every kind except IconGrid has nothing on it a click could ever need to reach.
        // IconGrid needs to receive the double-click that launches an item - WS_EX_NOACTIVATE
        // alone (still applied above) already stops it from ever becoming the foreground
        // window on click, so skipping WS_EX_TRANSPARENT here doesn't reopen any of the
        // focus-stealing issues ClickThroughWindow's WS_EX_TOOLWINDOW otherwise guards against.
        if (_widget.Kind != WidgetKind.IconGrid)
        {
            this.MakeClickThrough();
        }

        SourceInitialized += (_, _) =>
        {
            SendToTop();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        };
        Loaded += async (_, _) => await InitializeContentAsync();
        Activated += (_, _) => SendToTop();
        Closed += (_, _) =>
        {
            _clockTimer?.Stop();
            _statsTimer?.Stop();
        };
    }

    private void SendToTop()
    {
        var hwnd = new HWND(new WindowInteropHelper(this).Handle);
        SetWindowPos(hwnd, HWND.HWND_TOP, _widget.X, _widget.Y, _widget.Width, _widget.Height,
            SetWindowPosFlags.SWP_NOACTIVATE);
    }

    /// <summary>Swallows Alt+F4/system-menu Close - a widget can become the Win32-active
    /// window (see the Activated handler above), which would otherwise let Alt+F4 close it for
    /// real. Same AltF4Blocker helper TaskbarWindow/MainWindow/WallpaperWindow already use
    /// unconditionally.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (AltF4Blocker.IsSysCommandClose(msg, wParam))
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private async Task InitializeContentAsync()
    {
        switch (_widget.Kind)
        {
            case WidgetKind.Media:
                LoadMedia();
                break;
            case WidgetKind.Clock:
                StartClock();
                break;
            case WidgetKind.SystemStats:
                StartSystemStats();
                break;
            case WidgetKind.IconGrid:
                await LoadIconGridAsync();
                break;
        }
    }

    private void StartClock()
    {
        UpdateClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockTimeText = now.ToString("h:mm tt");
        ClockDateText = now.ToString("dddd, MMMM d");
    }

    private void StartSystemStats()
    {
        UpdateStats();
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();
    }

    private void UpdateStats()
    {
        var stats = SystemStats.Read();
        CpuText = $"CPU: {stats.CpuPercent:0}%";
        RamText = $"RAM: {stats.RamPercent:0}%";
    }

    private async Task LoadIconGridAsync()
    {
        var entries = await Task.Run(DesktopIconLoader.ListDesktop);
        foreach (var entry in entries)
        {
            IconEntries.Add(entry);
            _ = DesktopIconLoader.LoadIconAsync(entry);
        }
    }

    private void IconList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IconList.SelectedItem is not DesktopIconEntry entry)
        {
            return;
        }

        var targetPath = entry.IsDirectory
            ? "explorer.exe"
            : entry.FullPath;
        var arguments = entry.IsDirectory ? $"\"{entry.FullPath}\"" : null;

        Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true, Arguments = arguments ?? "" });
    }

    private void LoadMedia()
    {
        var mediaPath = _widget.MediaPath!;
        var uri = new Uri(MediaWrapperHtml.ToNavigableUrl(mediaPath));

        if (MediaWrapperHtml.IsVideo(mediaPath))
        {
            Video.Source = uri;
            Video.Play();
        }
        else
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            ImageSource = bitmap;
        }
    }

    /// <summary>Loops the video - MediaElement has no built-in loop flag.</summary>
    private void Video_MediaEnded(object sender, RoutedEventArgs e)
    {
        Video.Position = TimeSpan.Zero;
        Video.Play();
    }
}
