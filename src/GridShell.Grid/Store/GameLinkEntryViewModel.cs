using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using GridShell.Core.GameLibraries;
using GridShell.Core.Tiles;

namespace GridShell;

/// <summary>UI-bound wrapper around a GameLinkEntry for the Store's "Games" category - mirrors
/// StoreSceneViewModel's shape (plain model + mutable UI state for an async-loaded cover).</summary>
public sealed class GameLinkEntryViewModel : INotifyPropertyChanged
{
    public required GameLinkEntry Entry { get; init; }

    /// <summary>The raw filename (e.g. "Ace_Combat_2.7z") - never shown or searched on its own,
    /// only used where the literal filename matters (extension-sensitive download/extraction
    /// logic). Name below is what every other piece of this view model means.</summary>
    public string FileName => Entry.Name;

    /// <summary>A real game title, cleaned from Entry.Name's raw filename convention (repo-
    /// confirmed: underscores instead of spaces, plus the usual RomTitleCleaner region/revision
    /// tags) - both what the UI shows AND what gets searched against IGDB for cover art.
    /// Without this, "Ace_Combat_2.7z" would search IGDB for literally that string rather than
    /// "Ace Combat 2", which is the exact kind of search-term noise that risks matching the
    /// wrong game's cover (e.g. onto plain "Ace Combat" instead) - a different failure mode from
    /// GameLinkEntry's own strict exact-filename mirror grouping, but the same underlying worry.</summary>
    public string Name => RomTitleCleaner.CleanFileName(Entry.Name);

    public string SizeDisplay => Entry.SizeDisplay;
    public string MirrorCountDisplay => Entry.Mirrors.Count > 1 ? $"{Entry.Mirrors.Count} mirrors" : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private ImageSource? _cover;
    public ImageSource? Cover
    {
        get => _cover;
        set
        {
            _cover = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCover));
        }
    }

    public bool HasCover => Cover is not null;

    public string InitialLetter => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    private bool _isQueued;
    public bool IsQueued
    {
        get => _isQueued;
        set
        {
            _isQueued = value;
            OnPropertyChanged();
        }
    }
}
