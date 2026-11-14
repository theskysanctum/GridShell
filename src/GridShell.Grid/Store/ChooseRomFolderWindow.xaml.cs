using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GridShell.Core.Interop;
using GridShell.Core.Tiles;

namespace GridShell;

/// <summary>Lets the user pick which ROM root a Store "Games" category download lands under
/// before it's queued - the default root, or one of Game Manager's own configured extra ROM
/// folders (see GameManagerWindow's "Extra ROM folders" list) - labeled "Default" and by drive
/// letter respectively, matching how the Download Manager's own "save as a game ROM" picker
/// already shows these same roots. The actual destination is root + "\" + platformSlug, same
/// slug the chosen entry's JSON file came from, so RomScanner finds it in the right place
/// regardless of which root was picked.</summary>
public partial class ChooseRomFolderWindow : Window
{
    public string Prompt { get; }
    public List<FolderOption> Options { get; }

    public string? SelectedFolder { get; private set; }

    public ChooseRomFolderWindow(string gameName, string platformSlug, IEnumerable<string> roots)
    {
        Prompt = $"Where should \"{gameName}\" be saved?";
        Options = roots
            .Select(root => new FolderOption(LabelForRoot(root), Path.Combine(root, platformSlug)))
            .ToList();

        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();
        this.HideWindowIcon();
    }

    /// <summary>"Default" for the default ROMs root, otherwise the extra root's own drive
    /// letter (e.g. "D:\ROMs" -> "D") - falling back to the full path for a root with no drive
    /// letter at all (a UNC path), since there's no single letter that would mean anything there.</summary>
    private static string LabelForRoot(string root)
    {
        if (root.Equals(EmulationPlatforms.RomsRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return "Default";
        }

        var driveRoot = Path.GetPathRoot(root);
        return driveRoot is { Length: >= 2 } ? driveRoot[..1].ToUpperInvariant() : root;
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void FolderListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void Accept()
    {
        if (FolderListBox.SelectedItem is not FolderOption option)
        {
            return;
        }

        SelectedFolder = option.FullPath;
        DialogResult = true;
        Close();
    }

    public sealed record FolderOption(string Label, string FullPath);
}
