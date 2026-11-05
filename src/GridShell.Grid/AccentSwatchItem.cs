using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GridShell;

/// <summary>One square in Settings' accent color row — either a fixed preset or the trailing
/// "custom" square that opens a hex-entry dialog. IsSelected drives the checkmark overlay that
/// makes the active color obvious at a glance.</summary>
public class AccentSwatchItem : INotifyPropertyChanged
{
    public required string Hex { get; init; }
    public required Brush Brush { get; init; }
    public bool IsCustom { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
