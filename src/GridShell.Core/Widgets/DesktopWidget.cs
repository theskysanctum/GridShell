using System.IO;

namespace GridShell.Core.Widgets;

/// <summary>Serializes as a plain integer ordinal (no JsonStringEnumConverter configured) -
/// same gotcha as UiFontChoice. Append new members at the end, never insert, or existing
/// users' saved widgets.json entries silently reinterpret as the wrong kind.</summary>
public enum WidgetKind
{
    Media,
    Clock,
    IconGrid,
    SystemStats,
}

/// <summary>A small panel floating over the desktop - one of several kinds (see WidgetKind).
/// Media is the only kind that's non-interactive/click-through by nature (see
/// ClickThroughWindow); IconGrid deliberately isn't, since it needs to receive double-clicks.</summary>
public class DesktopWidget
{
    public required string Id { get; init; }
    public required WidgetKind Kind { get; init; }

    /// <summary>Only meaningful for Kind == Media - a local file path or http(s) URL.</summary>
    public string? MediaPath { get; init; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Display-friendly row text for the Settings "Widgets" list.</summary>
    public string SummaryText => Kind switch
    {
        WidgetKind.Media => $"{Path.GetFileName(MediaPath)} — {Width}×{Height} @ ({X},{Y})",
        WidgetKind.Clock => $"Clock — {Width}×{Height} @ ({X},{Y})",
        WidgetKind.IconGrid => $"Desktop Icons — {Width}×{Height} @ ({X},{Y})",
        WidgetKind.SystemStats => $"System Stats — {Width}×{Height} @ ({X},{Y})",
        _ => "Widget",
    };
}
