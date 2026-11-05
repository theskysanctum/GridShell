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

/// <summary>Lets the user search IGDB directly and pick the actual right cover for a game tile,
/// rather than trusting IgdbCoverService's automatic best-by-rating_count choice - useful for a
/// game with a common/reused title, a remaster vs. the original, or a region-specific release
/// that the automatic pick gets wrong.</summary>
public partial class ScrapeCoverWindow : Window, INotifyPropertyChanged
{
    private readonly string _clientId;
    private readonly string _clientSecret;

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
            }
        }
    }

    private bool _hasSearched;

    public bool ShowNoResults => _hasSearched && !IsSearching && Candidates.Count == 0;

    public ObservableCollection<CandidateRow> Candidates { get; } = new();

    public string? SelectedCoverPath { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ScrapeCoverWindow(string gameName, string clientId, string clientSecret)
    {
        _query = gameName;
        _clientId = clientId;
        _clientSecret = clientSecret;

        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();

        Loaded += async (_, _) =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
            await RunSearchAsync();
        };
    }

    private async Task RunSearchAsync()
    {
        IsSearching = true;
        Candidates.Clear();

        var results = await IgdbCoverService.SearchCandidatesAsync(Query, _clientId, _clientSecret);
        foreach (var candidate in results)
        {
            Candidates.Add(new CandidateRow(candidate));
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
        var path = await IgdbCoverService.DownloadCoverAsync(row.Candidate);
        IsSearching = false;

        if (path is null)
        {
            MessageBox.Show(this, "Couldn't download that cover - try another one.", "Scrape Cover Art",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
    public IgdbCoverService.IgdbCandidate Candidate { get; }
    public string Label { get; }
    public ImageSource Thumbnail { get; }

    public CandidateRow(IgdbCoverService.IgdbCandidate candidate)
    {
        Candidate = candidate;
        Label = candidate.ReleaseYear is { } year ? $"{candidate.Name} ({year})" : candidate.Name;
        Thumbnail = new BitmapImage(new Uri($"https://images.igdb.com/igdb/image/upload/t_cover_small/{candidate.ImageId}.jpg"));
    }
}
