using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridShell.Core.Interop;
using GridShell.Core.Tiles;

namespace GridShell;

/// <summary>Lets the user search a cover-art provider and choose the matching result.</summary>
public partial class ScrapeCoverWindow : Window, INotifyPropertyChanged
{
    private readonly SearchSource _searchSource;
    private readonly string _credential;
    private readonly string? _secondaryCredential;
    private readonly string? _preferredPlatform;

    private enum SearchSource
    {
        Igdb,
        TheGamesDb,
    }

    private string _query;
    public string Query
    {
        get => _query;
        set
        {
            if (_query != value)
            {
                _query = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (_isSearching != value)
            {
                _isSearching = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowNoResults));
                OnPropertyChanged(nameof(ShowSearchError));
            }
        }
    }

    private bool _hasSearched;

    private string? _searchErrorMessage;
    public string? SearchErrorMessage
    {
        get => _searchErrorMessage;
        private set
        {
            if (_searchErrorMessage != value)
            {
                _searchErrorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowNoResults));
                OnPropertyChanged(nameof(ShowSearchError));
            }
        }
    }

    public bool ShowNoResults => _hasSearched && !IsSearching && Candidates.Count == 0 && SearchErrorMessage is null;
    public bool ShowSearchError => !IsSearching && SearchErrorMessage is not null;

    public ObservableCollection<CandidateRow> Candidates { get; } = new();

    public string? SelectedCoverPath { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ScrapeCoverWindow(string gameName, string clientId, string clientSecret)
        : this(gameName, SearchSource.Igdb, clientId, clientSecret, null)
    {
    }

    public static ScrapeCoverWindow ForTheGamesDb(string gameName, string apiKey, string? preferredPlatform) =>
        new(gameName, SearchSource.TheGamesDb, apiKey, null, preferredPlatform);

    private ScrapeCoverWindow(
        string gameName,
        SearchSource searchSource,
        string credential,
        string? secondaryCredential,
        string? preferredPlatform)
    {
        _query = gameName;
        _searchSource = searchSource;
        _credential = credential;
        _secondaryCredential = secondaryCredential;
        _preferredPlatform = preferredPlatform;

        InitializeComponent();
        Title = searchSource == SearchSource.Igdb ? "Search IGDB Covers" : "Search TheGamesDB Covers";
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();
        this.HideWindowIcon();

        Loaded += async (_, _) =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
            await RunSearchAsync();
        };
    }

    private async Task RunSearchAsync()
    {
        if (IsSearching)
        {
            return;
        }

        IsSearching = true;
        SearchErrorMessage = null;
        Candidates.Clear();

        if (_searchSource == SearchSource.Igdb)
        {
            var result = await IgdbCoverService.SearchCandidatesAsync(Query, _credential, _secondaryCredential ?? "");
            foreach (var candidate in result.Candidates)
            {
                Candidates.Add(CandidateRow.ForIgdb(candidate));
            }

            SearchErrorMessage = result.ErrorMessage;
        }
        else
        {
            var result = await TheGamesDbCoverService.SearchCandidatesAsync(Query, _credential, _preferredPlatform);
            foreach (var candidate in result.Candidates)
            {
                Candidates.Add(CandidateRow.ForTheGamesDb(candidate));
            }

            SearchErrorMessage = result.ErrorMessage;
        }

        _hasSearched = true;
        IsSearching = false;
        OnPropertyChanged(nameof(ShowNoResults));
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await RunSearchAsync();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private async void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not CandidateRow row)
        {
            return;
        }

        IsSearching = true;
        var path = await row.DownloadCoverAsync();
        IsSearching = false;

        if (path is null)
        {
            // A plain MessageBox here was a jarring, unthemed native dialog sitting in the
            // middle of an otherwise dark/custom-styled window - same toast every other
            // "didn't work, try again" moment in the app already uses.
            new ToastWindow("Couldn't download that cover - try another one.", TimeSpan.FromSeconds(5), isWarning: true).Show();
            return;
        }

        SelectedCoverPath = path;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public class CandidateRow
{
    public string Label { get; }
    public ImageSource Thumbnail { get; }
    private readonly Func<Task<string?>> _downloadCoverAsync;

    private CandidateRow(string label, string thumbnailUrl, Func<Task<string?>> downloadCoverAsync)
    {
        Label = label;
        Thumbnail = new BitmapImage(new Uri(thumbnailUrl));
        _downloadCoverAsync = downloadCoverAsync;
    }

    public static CandidateRow ForIgdb(IgdbCoverService.IgdbCandidate candidate)
    {
        var label = candidate.ReleaseYear is { } year ? $"{candidate.Name} ({year})" : candidate.Name;
        var thumbnailUrl = $"https://images.igdb.com/igdb/image/upload/t_cover_small/{candidate.ImageId}.jpg";
        return new CandidateRow(label, thumbnailUrl, () => IgdbCoverService.DownloadCoverAsync(candidate));
    }

    public static CandidateRow ForTheGamesDb(TheGamesDbCoverService.Candidate candidate)
    {
        var label = candidate.ReleaseYear is { } year ? $"{candidate.Name} ({year})" : candidate.Name;
        if (!string.IsNullOrWhiteSpace(candidate.PlatformName))
        {
            label += $" - {candidate.PlatformName}";
        }

        return new CandidateRow(
            label,
            candidate.ThumbnailUrl,
            () => TheGamesDbCoverService.DownloadCoverAsync(candidate));
    }

    public Task<string?> DownloadCoverAsync() => _downloadCoverAsync();
}
