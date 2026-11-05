using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridShell;

/// <summary>Renders a Material Symbols glyph to a small bitmap for use as a Window.Icon - lets
/// a window's taskbar/title-bar icon reuse the same icon font already used throughout the UI
/// instead of needing a separate .ico asset shipped with the project.</summary>
public static class GlyphIcon
{
    public static ImageSource Render(char glyph, Brush background, Brush foreground, int size = 64)
    {
        // The relative "./Fonts/#Family" form only resolves automatically inside XAML (against
        // the consuming assembly) - from code-behind it needs an explicit pack-URI base, or
        // WPF treats it as an unknown system font name and falls back to tofu boxes.
        var typeface = new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Material Symbols Outlined"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var formattedText = new FormattedText(
            glyph.ToString(),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size * 0.62,
            foreground,
            1.0);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, size, size);
            var radius = size * 0.18;
            dc.DrawRoundedRectangle(background, null, rect, radius, radius);
            var x = (size - formattedText.Width) / 2;
            var y = (size - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(x, y));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
