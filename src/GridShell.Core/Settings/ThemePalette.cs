using System.Windows;
using System.Windows.Media;

namespace GridShell.Core.Settings;

/// <summary>Applies GridShell's dark custom-colored surfaces (window/tile/panel backgrounds,
/// text, and borders); WPF's built-in dark ThemeMode separately restyles standard controls.</summary>
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

    public static void Apply(ResourceDictionary resources)
    {
        resources["WindowBackgroundBrush"] = Brush("#1A1A1A");
        resources["SurfaceBackgroundBrush"] = Brush("#2A2A2A");
        resources["PanelBorderBrush"] = Brush("#555555");
        resources["PrimaryForegroundBrush"] = Brush("#FFFFFF");
        resources["MutedForegroundBrush"] = Brush("#AAAAAA");
        resources["FaintForegroundBrush"] = Brush("#777777");
        resources["ErrorForegroundBrush"] = Brush("#E08080");
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
