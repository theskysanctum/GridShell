using System.IO;
using System.Linq;
using System.Windows.Media;
using GridShell.Core.Settings;

namespace GridShell;

/// <summary>Turns the UiFont setting into an actual FontFamily - always with Roboto listed as
/// an explicit fallback, so a custom or Gaming font that's missing a glyph (a character its
/// own coverage doesn't include) falls back to the default instead of showing tofu, and a
/// custom font that fails to load outright falls back to plain Roboto.</summary>
public static class UiFontResolver
{
    private const string PackBase = "pack://application:,,,/";
    private const string Fallback = "./Fonts/#Roboto";

    public static FontFamily Resolve(AppSettings settings)
    {
        try
        {
            switch (settings.UiFont)
            {
                case UiFontChoice.Gaming:
                    return new FontFamily(new Uri(PackBase), $"./Fonts/#Exo 2,{Fallback}");

                case UiFontChoice.Rajdhani:
                    return new FontFamily(new Uri(PackBase), $"./Fonts/#Rajdhani,{Fallback}");

                case UiFontChoice.Orbitron:
                    return new FontFamily(new Uri(PackBase), $"./Fonts/#Orbitron,{Fallback}");

                case UiFontChoice.Custom when !string.IsNullOrWhiteSpace(settings.CustomFontPath)
                    && File.Exists(settings.CustomFontPath):
                    var custom = Fonts.GetFontFamilies(new Uri(settings.CustomFontPath)).FirstOrDefault();
                    if (custom is not null)
                    {
                        return new FontFamily(new Uri(PackBase), $"{custom.Source},{Fallback}");
                    }
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException)
        {
            // Falls through to the plain default below - a bad/unreadable custom font file
            // shouldn't leave the whole UI without a usable font.
        }

        return new FontFamily(new Uri(PackBase), Fallback);
    }
}
