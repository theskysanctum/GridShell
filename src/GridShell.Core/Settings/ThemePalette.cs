using System.Windows;
using System.Windows.Media;

namespace GridShell.Core.Settings;

/// <summary>Applies GridShell's own custom-colored surfaces (window/tile/panel backgrounds,
/// text, borders) for the selected theme — separate from WPF's built-in ThemeMode (Fluent light/
/// dark), which only restyles standard controls (buttons, combo boxes, scrollbars) and has no
/// effect on colors we set explicitly ourselves. Both need to be applied together for a
/// consistent look; see each App.xaml.cs's theme-apply step.</summary>
public static class ThemePalette
{
    public static Color DarkenAccent(Color color, double factor) =>
        Color.FromRgb(
            (byte)(color.R * factor),
            (byte)(color.G * factor),
            (byte)(color.B * factor));

    public static void ApplyAccent(ResourceDictionary resources, Color color)
    {
        var accentBrush = new SolidColorBrush(color);
        resources["AccentBrush"] = accentBrush;
        resources["AccentSecondaryColor"] = DarkenAccent(color, 0.48);
        resources["AccentOutlineBrush"] = CreateAccentOutlineBrush(color);
        resources[SystemColors.AccentColorKey] = color;
        resources[SystemColors.AccentColorLight1Key] = LightenAccent(color, 0.2);
        resources[SystemColors.AccentColorLight2Key] = LightenAccent(color, 0.4);
        resources[SystemColors.AccentColorLight3Key] = LightenAccent(color, 0.6);
        resources[SystemColors.AccentColorDark1Key] = DarkenAccent(color, 0.8);
        resources[SystemColors.AccentColorDark2Key] = DarkenAccent(color, 0.6);
        resources[SystemColors.AccentColorDark3Key] = DarkenAccent(color, 0.4);
    }

    private static Color LightenAccent(Color color, double factor) =>
        Color.FromRgb(
            (byte)(color.R + (byte.MaxValue - color.R) * factor),
            (byte)(color.G + (byte.MaxValue - color.G) * factor),
            (byte)(color.B + (byte.MaxValue - color.B) * factor));

    public static LinearGradientBrush CreateAccentOutlineBrush(Color color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(DarkenAccent(color, 0.88), 0));
        brush.GradientStops.Add(new GradientStop(DarkenAccent(color, 0.82), 1));
        return brush;
    }

    public static void Apply(ResourceDictionary resources, AppTheme theme)
    {
        var (window, surface, border, text, mutedText, faintText, error) = theme == AppTheme.Light
            ? ("#F3F3F3", "#FFFFFF", "#D0D0D0", "#1A1A1A", "#666666", "#999999", "#C0392B")
            : ("#1A1A1A", "#2A2A2A", "#555555", "#FFFFFF", "#AAAAAA", "#777777", "#E08080");

        resources["WindowBackgroundBrush"] = Brush(window);
        resources["SurfaceBackgroundBrush"] = Brush(surface);
        resources["PanelBorderBrush"] = Brush(border);
        resources["PrimaryForegroundBrush"] = Brush(text);
        resources["MutedForegroundBrush"] = Brush(mutedText);
        resources["FaintForegroundBrush"] = Brush(faintText);
        resources["ErrorForegroundBrush"] = Brush(error);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
