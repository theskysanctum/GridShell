using System.Text.RegularExpressions;

namespace GridShell.Core.Tiles;

/// <summary>Strips common ROM-naming-convention tags — region, language, revision, dump-
/// verification marks like "(USA)", "(En,Fr,Es)", "[!]", "(Rev 1)" — from a bare ROM filename so
/// it reads as a clean game title. Without this, a default (untyped) emulated-game title is the
/// raw filename, and IGDB's search has zero matches for something like
/// "Super Mario Galaxy (USA) (En,Fr,Es)" even though "Super Mario Galaxy" matches immediately.</summary>
public static class RomTitleCleaner
{
    public static string Clean(string fileNameWithoutExtension)
    {
        var withoutTags = Regex.Replace(fileNameWithoutExtension, @"[\(\[][^\)\]]*[\)\]]", "");
        var collapsed = Regex.Replace(withoutTags, @"\s{2,}", " ").Trim(' ', '-', '_');
        return collapsed.Length > 0 ? collapsed : fileNameWithoutExtension;
    }
}
