using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GridShell;

/// <summary>One icon in Settings' Start-button icon gallery - a curated set of fitting Material
/// Symbols glyphs the taskbar launcher button can show instead of its default. IsSelected
/// drives the highlight ring that makes the active icon obvious at a glance.</summary>
public class StartIconOption : INotifyPropertyChanged
{
    public required string Codepoint { get; init; }
    public required string Name { get; init; }

    public string Glyph => char.ConvertFromUtf32(int.Parse(Codepoint, System.Globalization.NumberStyles.HexNumber));

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
