using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Vanara.PInvoke;

namespace GridShell.Core.Taskbar;

public class TaskbarWindowEntry : INotifyPropertyChanged
{
    public required HWND Handle { get; init; }

    private string _title = "";
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Initial));
            }
        }
    }

    /// <summary>First letter of the title — fallback shown underneath the real icon for
    /// windows whose app icon couldn't be resolved.</summary>
    public string Initial => Title.Length > 0 ? Title[..1].ToUpperInvariant() : "?";

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    /// <summary>Drives hiding the Initial-letter fallback once a real icon is available — most
    /// real icons have transparent corners (e.g. a circular favicon), so without this the
    /// fallback letter shows through behind/around them instead of being fully replaced.</summary>
    public bool HasIcon => Icon is not null;

    /// <summary>True once Icon came from the window's own icon (not the exe-icon fallback) —
    /// lets the tracker stop retrying an entry once it has the real one, instead of re-querying
    /// every refresh forever.</summary>
    public bool HasOwnIcon { get; set; }

    private bool _isActive;

    /// <summary>Whether this window currently has foreground focus — drives the active-window
    /// indicator in the taskbar, same idea as a real taskbar highlighting the focused app.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
