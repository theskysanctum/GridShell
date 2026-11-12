using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GridShell.Core.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.DwmApi;
using static Vanara.PInvoke.User32;

namespace GridShell;

/// <summary>A controller-navigable on-screen keyboard, styled after the PS3 XMB's on-screen
/// keyboard — large grid of keys, a prominent text preview above it, dark rounded modal panel.
/// Types directly into whatever window has OS focus (like Windows' own on-screen keyboard),
/// never taking focus itself (WS_EX_NOACTIVATE), so it works system-wide, not just within
/// GridShell's own windows. Pre-warmed/reused across toggles, matching the grid's own pattern,
/// rather than being recreated each time.</summary>
public partial class OnScreenKeyboardWindow : Window, INotifyPropertyChanged
{
    private const double MoveDeadzone = 0.2;
    private const double MaxMovePixelsPerTick = 14.0;

    // Sub-pixel remainder carried between PollMove ticks - without this, every tick's movement
    // gets truncated to a whole pixel independently (SetWindowPos only takes integers), which at
    // low stick deflection rounds mostly down to 0 and makes the window visibly stutter/step
    // instead of gliding. Accumulating the fractional part here and only truncating the running
    // total fixes that.
    private double _moveAccumX;
    private double _moveAccumY;

    private readonly List<List<OskKey>> _rows;

    // D-pad only — the left stick is reserved purely for mouse movement everywhere (even here),
    // and the right stick instead moves this window around the screen (PollMove below).
    private readonly ControllerInput _controller = new(NavigationStick.None);
    private readonly DispatcherTimer _moveTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private static readonly TimeSpan ShiftDoubleTapWindow = TimeSpan.FromMilliseconds(350);

    private int _selectedRow;
    private int _selectedCol;
    private bool _isShift;
    private bool _capsLock;
    private DateTime _lastShiftTap = DateTime.MinValue;
    private OskKey? _shiftKey;
    private OskKey? _spaceKey;
    private List<OskKey> _modifierKeys = new();

    private string _text = "";
    public string Text
    {
        get => _text;
        private set
        {
            if (_text != value)
            {
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TextBeforeCursor));
                OnPropertyChanged(nameof(TextAfterCursor));
            }
        }
    }

    // Tracks where typing/backspace/arrow-key presses land in the preview text, independent of
    // Text itself always being appended to — needed now that Left/Right can move the cursor
    // without changing the string, and Char/Backspace need to act at the cursor, not just at
    // the end.
    private int _cursorPos;
    private int CursorPos
    {
        get => _cursorPos;
        set
        {
            var clamped = Math.Clamp(value, 0, Text.Length);
            if (_cursorPos != clamped)
            {
                _cursorPos = clamped;
                OnPropertyChanged(nameof(TextBeforeCursor));
                OnPropertyChanged(nameof(TextAfterCursor));
            }
        }
    }

    /// <summary>The preview text up to the cursor — split from TextAfterCursor by a blinking
    /// caret in the XAML, so the preview box shows where the next key will land.</summary>
    public string TextBeforeCursor => Text[..Math.Min(_cursorPos, Text.Length)];

    public string TextAfterCursor => Text[Math.Min(_cursorPos, Text.Length)..];

    public ObservableCollection<OskRow> Rows { get; } = new();

    // One real-keyboard-style stagger per row, in pixels — each key cell is 38px wide + 2px
    // margin on each side = 42px. The symbol row lines up directly above the number row it
    // belongs to. homeRow's Enter is its last (13th) key, so its left edge is 32 + 12*42 = 536 —
    // arrowTopRow's indent (536) puts Up directly under Enter, and mainBottomRow's indent (158)
    // is picked so Down (the 6th key: Ctrl, Alt, Win, Space, Left, Down) lands at that same 536,
    // completing the inverted-T directly below Up.
    private static readonly double[] RowIndents =
        { 0, 0, 21, 32, 53, 536, 158 };

    // Rows 5 and 6 (the arrow cluster and the Ctrl/Alt/Win/Space/arrows row under it) otherwise
    // read as floating with visible empty space above/within them - negative top margins pull
    // both snug against bottomRow and each other, closer to how tight a real keyboard's bottom
    // cluster actually sits.
    private const int ArrowTopRowIndex = 5;
    private const double ArrowTopRowNegativeMargin = -24;
    private const int MainBottomRowIndex = 6;
    private const double MainBottomRowNegativeMargin = -6;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public OnScreenKeyboardWindow()
    {
        _rows = BuildLayout();
        _shiftKey = _rows.SelectMany(r => r).FirstOrDefault(k => k.Action == OskKeyAction.Shift);
        _spaceKey = _rows.SelectMany(r => r).FirstOrDefault(k => k.Action == OskKeyAction.Space);
        _modifierKeys = _rows.SelectMany(r => r).Where(k => k.Action == OskKeyAction.Modifier).ToList();

        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();
        this.NeverActivate();
        this.IgnoreMouseInput();

        for (var i = 0; i < _rows.Count; i++)
        {
            var indent = i < RowIndents.Length ? RowIndents[i] : 0;
            var topMargin = i switch
            {
                ArrowTopRowIndex => ArrowTopRowNegativeMargin,
                MainBottomRowIndex => MainBottomRowNegativeMargin,
                _ => 0,
            };
            Rows.Add(new OskRow
            {
                Keys = new ObservableCollection<OskKey>(_rows[i]),
                Indent = indent,
                Margin = new Thickness(indent, topMargin, 0, 0),
            });
        }

        SelectKey(0, 0);

        _controller.ActionTriggered += OnControllerAction;
        _moveTimer.Tick += (_, _) => PollMove();

        SourceInitialized += (_, _) => ApplyBlurBehind();
    }

    /// <summary>The classic Aero "blur behind" API (DwmEnableBlurBehindWindow), not the newer
    /// Mica/Acrylic DWMWA_SYSTEMBACKDROP_TYPE one - that one needs a normal (non-layered) window
    /// and was already tried and reverted for the grid because AllowsTransparency="True" (this
    /// window's own WS_EX_LAYERED) is fundamentally incompatible with it. Blur-behind predates
    /// that restriction and is specifically meant for layered/transparent windows like this one,
    /// so it has an actual chance of working where that attempt didn't. Deprecated since Windows
    /// 8 but still functional through current Windows versions.</summary>
    private void ApplyBlurBehind()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var blurBehind = new DWM_BLURBEHIND(true);
        DwmEnableBlurBehindWindow(new HWND(hwnd), blurBehind);
    }

    /// <summary>Opens this if it's closed; does nothing if it's already open - Y/Triangle (the
    /// same button that opens this) now types a space while it's already open instead, so this
    /// is no longer a toggle. Done is the only way to close it.</summary>
    public void OpenIfClosed()
    {
        if (!IsVisible)
        {
            ShowOverlay();
        }
    }

    private void ShowOverlay()
    {
        // Fresh each time it's opened — it's typing into a new, unknown context each time,
        // not continuing a previous session's text.
        Text = "";
        CursorPos = 0;
        SelectKey(0, 0);
        _isShift = false;
        _capsLock = false;
        _lastShiftTap = DateTime.MinValue;
        ClearModifiers();
        UpdateLetterCase();
        UpdateShiftKeyVisual();

        // Show (not Activate) — WS_EX_NOACTIVATE means this genuinely never takes focus, so
        // whatever the user was typing into keeps it the whole time this is visible.
        Show();
        BringAboveOthers();
        _controller.Start();
        _moveTimer.Start();
    }

    /// <summary>Re-asserts HWND_TOPMOST, pushing this to the very front of the topmost band.
    /// Topmost="True" alone doesn't guarantee ordering BETWEEN two topmost windows (the grid
    /// is also topmost) — whichever was Z-ordered more recently wins. Called both when this
    /// shows itself and by the grid after IT re-asserts its own topmost status, so the keyboard
    /// stays above the grid no matter which one was toggled more recently.</summary>
    public void BringAboveOthers()
    {
        if (!IsVisible)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    private void HideOverlay()
    {
        _controller.Stop();
        _moveTimer.Stop();
        Hide();
    }

    private bool _nintendoLayoutHints;

    /// <summary>Nintendo's physical A/B/X/Y layout is rotated 180° from Xbox/PlayStation's (its
    /// "A" sits where Xbox's "B" does, "X"/"Y" are swapped too) - swap the labels shown in the
    /// hint bar to match whatever's actually printed on a connected Switch-layout pad, rather
    /// than always showing Xbox/PlayStation labels. Nothing about which physical button performs
    /// which action changes - purely the displayed text.</summary>
    public string HintText => _nintendoLayoutHints
        ? "D-pad: move   •   R-stick: move window   •   R3: shift   •   Cross/B: type   •   Square/Y: backspace   •   Triangle/X: space   •   Done: close"
        : "D-pad: move   •   R-stick: move window   •   R3: shift   •   Cross/A: type   •   Square/X: backspace   •   Triangle/Y: space   •   Done: close";

    private void PollMove()
    {
        var nintendoLayout = PadReader.IsAnyNintendoControllerConnected();
        if (nintendoLayout != _nintendoLayoutHints)
        {
            _nintendoLayoutHints = nintendoLayout;
            OnPropertyChanged(nameof(HintText));
        }

        if (PadReader.Read() is not { } reading)
        {
            return;
        }

        var (x, y) = ApplyRadialDeadzone(reading.RightX, reading.RightY);
        if (x == 0 && y == 0)
        {
            // Nothing pushed this tick - drop any sub-pixel remainder rather than letting it
            // sit around and cause a small delayed jump whenever the stick is touched again.
            _moveAccumX = 0;
            _moveAccumY = 0;
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(new HWND(hwnd), out var rect))
        {
            return;
        }

        _moveAccumX += x * MaxMovePixelsPerTick;
        // Screen Y grows downward; stick Y grows upward — invert, same as the mouse emulation.
        _moveAccumY += -y * MaxMovePixelsPerTick;

        var dx = (int)_moveAccumX;
        var dy = (int)_moveAccumY;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        // Keep the fractional remainder for next tick instead of discarding it - this is what
        // actually makes slow movement glide instead of stepping in whole-pixel jumps.
        _moveAccumX -= dx;
        _moveAccumY -= dy;

        var workArea = AppBarManager.GetWorkArea();
        var width = rect.right - rect.left;
        var height = rect.bottom - rect.top;

        var newLeft = Math.Clamp(rect.left + dx, workArea.left, Math.Max(workArea.left, workArea.right - width));
        var newTop = Math.Clamp(rect.top + dy, workArea.top, Math.Max(workArea.top, workArea.bottom - height));

        SetWindowPos(new HWND(hwnd), HWND.NULL, newLeft, newTop, 0, 0,
            SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    /// <summary>Radial (not per-axis) deadzone - scales the whole vector smoothly from 0 right
    /// at the deadzone edge up to full speed at max deflection, instead of ApplyDeadzone's old
    /// per-axis hard cutoff, which made the window jump straight to ~20% speed the instant the
    /// stick crossed the threshold rather than ramping in.</summary>
    private static (double X, double Y) ApplyRadialDeadzone(double x, double y)
    {
        var magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude < MoveDeadzone)
        {
            return (0, 0);
        }

        var scale = Math.Min(1.0, (magnitude - MoveDeadzone) / (1.0 - MoveDeadzone)) / magnitude;
        return (x * scale, y * scale);
    }

    private static List<List<OskKey>> BuildLayout()
    {
        OskKey Letter(char c) => new() { Display = c.ToString(), Action = OskKeyAction.Char, Char = c };
        OskKey Sym(char c, char shifted) => new() { Display = c.ToString(), Action = OskKeyAction.Char, Char = c, ShiftChar = shifted };
        OskKey PlainChar(char c) => new() { Display = c.ToString(), Action = OskKeyAction.Char, Char = c };
        OskKey SendKey(string display, VK vk) => new() { Display = display, Action = OskKeyAction.SendKey, Vk = vk };

        // Its own dedicated key for each number's symbol, directly above it — no Shift needed
        // to reach it, unlike the remaining symbol keys elsewhere that still use a badge.
        var symbolRow = "~!@#$%^&*()_+".Select(PlainChar).ToList();

        var numberRow = "`1234567890-=".Select(PlainChar).ToList();
        numberRow.Add(
            new OskKey { Display = "Backspace", IconGlyph = "", Action = OskKeyAction.Backspace });

        var topRow = new List<OskKey> { SendKey("Tab", VK.VK_TAB) }
            .Concat("qwertyuiop".Select(Letter))
            .Append(Sym('[', '{'))
            .Append(Sym(']', '}'))
            .Append(Sym('\\', '|'))
            .ToList();

        var homeRow = new List<OskKey> { new() { Display = "Shift", IconGlyph = "", Action = OskKeyAction.Shift } }
            .Concat("asdfghjkl".Select(Letter))
            .Append(Sym(';', ':'))
            .Append(Sym('\'', '"'))
            .Append(SendKey("Enter", VK.VK_RETURN))
            .ToList();

        var bottomRow = "zxcvbnm".Select(Letter)
            .Append(Sym(',', '<'))
            .Append(Sym('.', '>'))
            .Append(Sym('/', '?'))
            .ToList();

        // The classic inverted-T: Up alone on its own row, directly above Down in the
        // merged bottom row below (RowIndents lines them up on the same column, under
        // homeRow's Enter key) - not a single flat row of four.
        var arrowTopRow = new List<OskKey>
        {
            SendKey("Up", VK.VK_UP),
        };

        // The actual bottom row of a real keyboard: modifiers, a wide space bar, then the
        // arrow cluster - immediately after bottomRow (the letters), not a separate
        // modifier row floating above the space bar.
        var mainBottomRow = new List<OskKey>
        {
            new() { Display = "Ctrl", IconGlyph = "", Action = OskKeyAction.Modifier, Vk = VK.VK_CONTROL },
            new() { Display = "Alt", IconGlyph = "", Action = OskKeyAction.Modifier, Vk = VK.VK_MENU },
            new() { Display = "Win", IconGlyph = "", Action = OskKeyAction.Modifier, Vk = VK.VK_LWIN },
            new() { Display = "Space", IconGlyph = "", Action = OskKeyAction.Space },
            SendKey("Left", VK.VK_LEFT),
            SendKey("Down", VK.VK_DOWN),
            SendKey("Right", VK.VK_RIGHT),
            new() { Display = "Done", IconGlyph = "", Action = OskKeyAction.Done },
        };

        return new List<List<OskKey>>
        {
            symbolRow, numberRow, topRow, homeRow, bottomRow, arrowTopRow, mainBottomRow,
        };
    }

    private void OnControllerAction(ControllerAction action)
    {
        switch (action)
        {
            case ControllerAction.Up: Move(-1, 0); break;
            case ControllerAction.Down: Move(1, 0); break;
            case ControllerAction.Left: Move(0, -1); break;
            case ControllerAction.Right: Move(0, 1); break;
            case ControllerAction.Accept: PressSelectedKey(); break;
            case ControllerAction.Backspace: Backspace(); break;
            case ControllerAction.Shift: ToggleShift(); break;

            // Y/Triangle - same idea as X/Backspace: a quick way to type without navigating to
            // the dedicated key. Only reachable while this window is visible (_controller only
            // runs then), so it can't fire from the Grid's own use of the same button to open
            // this in the first place - see MainWindow's OpenKeyboardMessage handler, which
            // stopped treating this as a toggle for the same reason: Done is the only way to
            // close this now, not Back or Y.
            case ControllerAction.OpenKeyboard:
                if (_spaceKey is not null)
                {
                    PressKey(_spaceKey);
                }

                break;
        }
    }

    private void Move(int rowDelta, int colDelta)
    {
        var newRow = Math.Clamp(_selectedRow + rowDelta, 0, _rows.Count - 1);
        var newCol = Math.Clamp(_selectedCol + colDelta, 0, _rows[newRow].Count - 1);
        SelectKey(newRow, newCol);
    }

    private void SelectKey(int row, int col)
    {
        if (_rows.Count > 0 && _rows[_selectedRow].Count > _selectedCol)
        {
            _rows[_selectedRow][_selectedCol].IsSelected = false;
        }

        _selectedRow = row;
        _selectedCol = Math.Min(col, _rows[row].Count - 1);
        _rows[_selectedRow][_selectedCol].IsSelected = true;
    }

    private void PressSelectedKey() => PressKey(_rows[_selectedRow][_selectedCol]);

    private void PressKey(OskKey key)
    {
        switch (key.Action)
        {
            case OskKeyAction.Char:
                var c = char.IsLetter(key.Char)
                    ? (_isShift ? char.ToUpperInvariant(key.Char) : char.ToLowerInvariant(key.Char))
                    : (_isShift && key.ShiftChar is char shifted ? shifted : key.Char);

                var charModifiers = ActiveModifierVks();
                if (charModifiers.Count > 0 && TryGetVkForChar(c, out var charVk))
                {
                    // A modifier combo (e.g. Ctrl+C) runs a command rather than typing text, so
                    // the preview isn't updated — there's nothing to show as "typed".
                    SyntheticKeyboard.PressKeyWithModifiers(charVk, charModifiers);
                    ClearModifiers();
                }
                else
                {
                    SyntheticKeyboard.TypeChar(c);
                    InsertAtCursor(c);
                }

                // A one-shot tap only shifts the one key just typed; caps lock stays on.
                if (_isShift && !_capsLock)
                {
                    _isShift = false;
                    UpdateLetterCase();
                    UpdateShiftKeyVisual();
                }
                break;

            case OskKeyAction.SendKey:
                var sendKeyModifiers = ActiveModifierVks();
                if (sendKeyModifiers.Count > 0)
                {
                    SyntheticKeyboard.PressKeyWithModifiers(key.Vk, sendKeyModifiers);
                    ClearModifiers();
                    break;
                }

                SyntheticKeyboard.PressKey(key.Vk);

                // Best-effort — only Left/Right have an obvious effect on the preview's own
                // cursor; Tab/Enter move focus or insert a newline in the real target, which the
                // single-field preview can't meaningfully mirror.
                if (key.Vk == VK.VK_LEFT)
                {
                    CursorPos--;
                }
                else if (key.Vk == VK.VK_RIGHT)
                {
                    CursorPos++;
                }
                break;

            case OskKeyAction.Space:
                var spaceModifiers = ActiveModifierVks();
                if (spaceModifiers.Count > 0)
                {
                    SyntheticKeyboard.PressKeyWithModifiers(VK.VK_SPACE, spaceModifiers);
                    ClearModifiers();
                }
                else
                {
                    SyntheticKeyboard.TypeChar(' ');
                    InsertAtCursor(' ');
                }
                break;

            case OskKeyAction.Backspace:
                var backspaceModifiers = ActiveModifierVks();
                if (backspaceModifiers.Count > 0)
                {
                    SyntheticKeyboard.PressKeyWithModifiers(VK.VK_BACK, backspaceModifiers);
                    ClearModifiers();
                }
                else
                {
                    Backspace();
                }
                break;

            case OskKeyAction.Modifier:
                key.IsActive = !key.IsActive;
                break;

            case OskKeyAction.Shift:
                ToggleShift();
                break;

            case OskKeyAction.Done:
                HideOverlay();
                break;
        }
    }

    private void Backspace()
    {
        SyntheticKeyboard.PressKey(VK.VK_BACK);
        if (_cursorPos > 0)
        {
            Text = Text[..(_cursorPos - 1)] + Text[_cursorPos..];
            CursorPos--;
        }
    }

    private void InsertAtCursor(char c)
    {
        Text = Text[.._cursorPos] + c + Text[_cursorPos..];
        CursorPos++;
    }

    private void UpdateLetterCase()
    {
        // Symbol keys keep their base character as Display always — their shift variant is
        // shown as a small secondary badge (ShiftDisplay) instead, like a real keycap.
        foreach (var key in _rows.SelectMany(r => r).Where(k => k.Action == OskKeyAction.Char && char.IsLetter(k.Char)))
        {
            key.Display = _isShift ? char.ToUpperInvariant(key.Char).ToString() : char.ToLowerInvariant(key.Char).ToString();
        }
    }

    /// <summary>Same one-shot/double-tap-for-lock behavior as tapping the on-screen Shift key,
    /// shared by that key press and the right-stick-click (R3) controller shortcut.</summary>
    private void ToggleShift()
    {
        var now = DateTime.UtcNow;
        var isDoubleTap = now - _lastShiftTap <= ShiftDoubleTapWindow;
        _lastShiftTap = now;

        if (_capsLock)
        {
            // Any tap while locked turns it off entirely.
            _capsLock = false;
            _isShift = false;
        }
        else if (isDoubleTap)
        {
            _capsLock = true;
            _isShift = true;
        }
        else
        {
            // A single tap is one-shot — shifts just the next key, toggled off by a
            // second (non-double-tap) press if the user changes their mind.
            _isShift = !_isShift;
        }

        UpdateLetterCase();
        UpdateShiftKeyVisual();
    }

    private void UpdateShiftKeyVisual()
    {
        if (_shiftKey is null)
        {
            return;
        }

        _shiftKey.IsShiftActive = _isShift;
        _shiftKey.IsCapsLocked = _capsLock;
    }

    private List<VK> ActiveModifierVks() => _modifierKeys.Where(k => k.IsActive).Select(k => k.Vk).ToList();

    private void ClearModifiers()
    {
        foreach (var key in _modifierKeys)
        {
            key.IsActive = false;
        }
    }

    /// <summary>Maps a character to its virtual-key code for a modifier combo (Ctrl+C etc.) —
    /// only letters and digits, where VK_A..VK_Z/VK_0..VK_9 happen to equal the ASCII codepoint.
    /// Symbol combos (rare, and layout-dependent) just aren't supported.</summary>
    private static bool TryGetVkForChar(char c, out VK vk)
    {
        if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
        {
            vk = (VK)char.ToUpperInvariant(c);
            return true;
        }

        if (c is >= '0' and <= '9')
        {
            vk = (VK)c;
            return true;
        }

        vk = default;
        return false;
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is OskKey key)
        {
            PressKey(key);
        }
    }

}
