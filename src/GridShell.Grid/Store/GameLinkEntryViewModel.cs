using System.ComponentModel;
using System.IO;
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

    /// <summary>Filename without its extension and with underscores replaced by spaces. Keep
    /// parenthetical qualifiers such as demos and revisions visible so distinct releases aren't
    /// presented as if they were the same game.</summary>
    public string Name => Path.GetFileNameWithoutExtension(Entry.Name).Replace('_', ' ');

    /// <summary>Cleaned title for IGDB cover lookup; unlike the Store label, the lookup should
    /// omit filename tags that make matching the base game less reliable.</summary>
    public string CoverSearchName => RomTitleCleaner.CleanFileName(Entry.Name);

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
