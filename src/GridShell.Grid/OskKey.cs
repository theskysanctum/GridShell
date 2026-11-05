using System.ComponentModel;
using System.Runtime.CompilerServices;
using static Vanara.PInvoke.User32;

namespace GridShell;

public enum OskKeyAction
{
    Char,
    Space,
    Backspace,
    Shift,
    Done,

    /// <summary>Sends a plain virtual-key press (Tab, Enter, arrow keys) — for keys that don't
    /// correspond to typed text, unlike Char/Space which go through SyntheticKeyboard.TypeChar.</summary>
    SendKey,

    /// <summary>Ctrl/Alt/Win — toggles on/off; while any are active, the next real key (Char/
    /// SendKey/Space/Backspace) is sent as a held-modifier combo instead of plain text, then all
    /// clear (one-shot, same spirit as Shift's single-tap).</summary>
    Modifier,
}

/// <summary>One key in the on-screen keyboard grid. IsSelected drives the highlighted-key
/// visual, same pattern as the accent color swatches' selection state.</summary>
public class OskKey : INotifyPropertyChanged
{
    /// <summary>Material Symbols glyph shown for special keys (Backspace/Shift/Space/Done);
    /// null for plain letter/number keys, which just show Display.</summary>
    public string? IconGlyph { get; init; }

    public required OskKeyAction Action { get; init; }
    public char Char { get; init; }

    /// <summary>The character typed instead of <see cref="Char"/> while Shift is active — null
    /// for letter keys, which use upper/lower-casing of Char instead.</summary>
    public char? ShiftChar { get; init; }

    /// <summary>ShiftChar as a bindable string, for the small secondary badge shown on symbol
    /// keys (like a real keycap printing both characters) — empty when there isn't one, which
    /// collapses the badge via StringToVisibilityConverter.</summary>
    public string ShiftDisplay => ShiftChar?.ToString() ?? "";

    /// <summary>The virtual-key code sent for Action == SendKey.</summary>
    public VK Vk { get; init; }

    private string _display = "";
    public required string Display
    {
        get => _display;
        set
        {
            if (_display != value)
            {
                _display = value;
                OnPropertyChanged();
            }
        }
    }

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

    // Only ever set on the Shift key itself — a one-shot tap (IsShiftActive) vs. a double-tap
    // caps lock (IsCapsLocked), so the key can show which state it's in.
    private bool _isShiftActive;
    public bool IsShiftActive
    {
        get => _isShiftActive;
        set
        {
            if (_isShiftActive != value)
            {
                _isShiftActive = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isCapsLocked;
    public bool IsCapsLocked
    {
        get => _isCapsLocked;
        set
        {
            if (_isCapsLocked != value)
            {
                _isCapsLocked = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Only ever set on a Modifier key — whether it's currently toggled on.</summary>
    private bool _isActive;
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

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
