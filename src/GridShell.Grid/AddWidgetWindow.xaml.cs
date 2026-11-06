using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using GridShell.Core;
using GridShell.Core.Interop;
using GridShell.Core.Widgets;

namespace GridShell;

/// <summary>Small modal for adding one desktop widget - a Kind picker (Media/Clock/Desktop
/// Icons/System Stats, same RequestedKind-switch-panel-visibility pattern as AddTileWindow)
/// plus a position/size, matching AddTileWindow's role (separate dialog, ShowDialog,
/// ResultWidget set on confirm). Position/size are plain numeric fields for v1 - live
/// drag-to-position/resize directly on the desktop is a good v2 enhancement, not built now.</summary>
public partial class AddWidgetWindow : Window, INotifyPropertyChanged
{
    private WidgetKind _kind = WidgetKind.Media;
    private string _mediaPath = "";
    private int _x;
    private int _y;
    private int _widgetWidth = 320;
    private int _widgetHeight = 240;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public WidgetKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMediaKind));
            OnPropertyChanged(nameof(IsClockKind));
            OnPropertyChanged(nameof(IsIconGridKind));
            OnPropertyChanged(nameof(IsSystemStatsKind));
        }
    }

    public bool IsMediaKind => Kind == WidgetKind.Media;
    public bool IsClockKind => Kind == WidgetKind.Clock;
    public bool IsIconGridKind => Kind == WidgetKind.IconGrid;
    public bool IsSystemStatsKind => Kind == WidgetKind.SystemStats;

    public string MediaPath
    {
        get => _mediaPath;
        set { _mediaPath = value; OnPropertyChanged(); }
    }

    public int X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    public int Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
    }

    public int WidgetWidth
    {
        get => _widgetWidth;
        set { _widgetWidth = value; OnPropertyChanged(); }
    }

    public int WidgetHeight
    {
        get => _widgetHeight;
        set { _widgetHeight = value; OnPropertyChanged(); }
    }

    public DesktopWidget? ResultWidget { get; private set; }

    public AddWidgetWindow()
    {
        InitializeComponent();
        DataContext = this;
        this.HideWindowIcon();

        X = (int)(SystemParameters.PrimaryScreenWidth / 2) - _widgetWidth / 2;
        Y = (int)(SystemParameters.PrimaryScreenHeight / 2) - _widgetHeight / 2;
    }

    private void MediaKind_Click(object sender, RoutedEventArgs e) => Kind = WidgetKind.Media;
    private void ClockKind_Click(object sender, RoutedEventArgs e) => Kind = WidgetKind.Clock;
    private void IconGridKind_Click(object sender, RoutedEventArgs e) => Kind = WidgetKind.IconGrid;
    private void SystemStatsKind_Click(object sender, RoutedEventArgs e) => Kind = WidgetKind.SystemStats;

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var extensions = string.Join(";",
            MediaWrapperHtml.ImageExtensions.Concat(MediaWrapperHtml.VideoExtensions).Select(ext => "*" + ext));

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an image or video",
            Filter = $"Media files ({extensions})|{extensions}|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            MediaPath = dialog.FileName;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (Kind == WidgetKind.Media && string.IsNullOrWhiteSpace(MediaPath))
        {
            return;
        }

        ResultWidget = new DesktopWidget
        {
            Id = Guid.NewGuid().ToString(),
            Kind = Kind,
            MediaPath = Kind == WidgetKind.Media ? MediaPath : null,
            X = X,
            Y = Y,
            Width = WidgetWidth,
            Height = WidgetHeight,
        };
        DialogResult = true;
        Close();
    }
}
