using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using GridShell.Core.Store;

namespace GridShell;

/// <summary>UI-bound wrapper around a StoreScene - mirrors StoreAppViewModel's shape (plain
/// model + mutable UI state) for the same reason: INotifyPropertyChanged for IsCurrent (changes
/// live as Scenes are applied) without polluting the plain data model.</summary>
public sealed class StoreSceneViewModel : INotifyPropertyChanged
{
    public required StoreScene Scene { get; init; }

    public string Name => Scene.Name;
    public string Description => Scene.Description;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private BitmapImage? _thumbnail;
    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool HasThumbnail => Thumbnail is not null;

    public string InitialLetter => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            _isCurrent = value;
            OnPropertyChanged();
        }
    }

    private bool _isApplying;
    public bool IsApplying
    {
        get => _isApplying;
        set
        {
            _isApplying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ApplyButtonText));
        }
    }

    public string ApplyButtonText => IsApplying ? "Downloading..." : "Apply";
}
