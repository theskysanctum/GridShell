using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GridShell.Core.Interop;
using GridShell.Core.Store;

namespace GridShell;

/// <summary>GridShell's app store - a curated catalog (synced from GridShell's own repo via
/// StoreCatalogSync, so it can be updated without a GridShell release) installed through winget
/// (WingetInstaller). Theme-aware throughout, same DynamicResource brushes as every other
/// window.</summary>
public partial class StoreWindow : Window, INotifyPropertyChanged
{
    private const string AllCategoriesLabel = "All Apps";

    private List<StoreAppViewModel> _allApps = new();

    public ObservableCollection<StoreAppViewModel> Apps { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    private string _selectedCategory = AllCategoriesLabel;
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
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

    public StoreWindow()
    {
        InitializeComponent();
        DataContext = this;

        Loaded += StoreWindow_Loaded;
    }

    private async void StoreWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var catalog = await StoreCatalogSync.SyncAndLoadAsync();
        _allApps = catalog.Apps
            .OrderBy(a => a.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(a => new StoreAppViewModel { App = a })
            .ToList();

        CatalogStatusText = _allApps.Count == 0
            ? "Couldn't load the catalog - check your internet connection and reopen the Store."
            : $"{_allApps.Count} apps available";

        Categories.Clear();
        Categories.Add(AllCategoriesLabel);
        foreach (var category in _allApps.Select(a => a.Category).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
        {
            Categories.Add(category);
        }

        // Re-push SelectedCategory now that Categories actually has entries - the ListBox's
        // SelectedItem binding has nothing to match against until now, so it wouldn't otherwise
        // show "All Apps" selected despite that already being the property's value.
        SelectedCategory = AllCategoriesLabel;

        _ = RefreshInstalledStateAsync();
    }

    /// <summary>Opening the Store previously always showed every app as not-installed, since
    /// each StoreAppViewModel starts that way and nothing checked reality against it - fixed by
    /// querying winget's own "already installed" check (WingetInstaller.IsInstalledAsync, same
    /// `winget list --id X -e` check InstallAsync uses) for every catalog entry on load.
    /// Throttled to a handful at a time rather than firing 20+ winget.exe processes at once.</summary>
    private async Task RefreshInstalledStateAsync()
    {
        using var throttle = new SemaphoreSlim(4);

        var checks = _allApps.Select(async app =>
        {
            await throttle.WaitAsync();
            try
            {
                app.State = StoreAppState.Checking;
                var installed = await WingetInstaller.IsInstalledAsync(app.App.WingetId);
                app.State = installed ? StoreAppState.Installed : StoreAppState.NotInstalled;
                if (installed)
                {
                    SetIconIfFound(app);
                }
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(checks);
    }

    /// <summary>Only ever has something to show once an app is actually installed - there's no
    /// icon to extract before then, so the UI keeps showing the InitialLetter fallback square
    /// until this finds a real exe.</summary>
    private static void SetIconIfFound(StoreAppViewModel app)
    {
        var exePath = InstalledAppLocator.FindMainExe(app.App.Name);
        if (exePath is not null)
        {
            app.Icon = ShellIcons.GetIcon(exePath);
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text;
        IEnumerable<StoreAppViewModel> matches = _allApps;

        if (SelectedCategory != AllCategoriesLabel)
        {
            matches = matches.Where(a => a.Category == SelectedCategory);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            matches = matches.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Apps.Clear();
        foreach (var app in matches)
        {
            Apps.Add(app);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not StoreAppViewModel app)
        {
            return;
        }

        app.State = StoreAppState.Installing;
        var result = await WingetInstaller.InstallAsync(app.App.WingetId);

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
            EmulatorAutoWire.TryWire(app.App);
            SetIconIfFound(app);
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
