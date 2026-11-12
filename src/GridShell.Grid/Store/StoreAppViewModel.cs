using System.ComponentModel;
using System.Runtime.CompilerServices;
using GridShell.Core.Store;

namespace GridShell;

public enum StoreAppState
{
    NotInstalled,
    Checking,
    Installing,
    Installed,
    Failed,
}

/// <summary>One catalog entry plus its live install state - the catalog itself (name/
/// description/category/winget id) never changes at runtime, only State/ErrorMessage do, as
/// WingetInstaller runs.</summary>
public sealed class StoreAppViewModel : INotifyPropertyChanged
{
    public required StoreApp App { get; init; }

    public string Name => App.Name;
    public string Description => App.Description;
    public string Category => App.Category;
    public string InitialLetter => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    private StoreAppState _state = StoreAppState.NotInstalled;
    public StoreAppState State
    {
        get => _state;
        set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanInstall));
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool CanInstall => State is StoreAppState.NotInstalled or StoreAppState.Failed;

    public string StatusText => State switch
    {
        StoreAppState.Checking => "Checking...",
        StoreAppState.Installing => "Installing...",
        StoreAppState.Installed => "Installed",
        StoreAppState.Failed => ErrorMessage ?? "Failed",
        _ => "",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
