using System.Collections.ObjectModel;
using System.Windows;

namespace GridShell;

/// <summary>One row of the on-screen keyboard's key grid, plus how far it's indented from the
/// left — a plain left/center-aligned ItemsControl per row produces a jagged staircase once rows
/// have different key counts (number row vs. the short arrow/space row), not the gentle,
/// consistent stagger a real keyboard has.</summary>
public class OskRow
{
    public required ObservableCollection<OskKey> Keys { get; init; }
    public double Indent { get; init; }

    /// <summary>Usually just Indent as a left margin - negative on the arrow-cluster row so it
    /// sits snug under the letter rows instead of with a visible gap (see
    /// OnScreenKeyboardWindow's RowTopMarginOverrides).</summary>
    public Thickness Margin { get; init; }
}
