using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GridShell.Core.Interop;

namespace GridShell;

/// <summary>GridShell's own dual-pane file manager - two independently-navigable panes (left/
/// right), full controller support on top of mouse/keyboard. Covers browsing, copy/move/
/// delete/rename/new-folder, and archive extract/compress (via whichever of 7-Zip/PeaZip is
/// installed) through the Start/Menu actions popup. Real Windows shell right-click context
/// menus are deliberately out of scope - the user asked for archiver support specifically,
/// not the full IContextMenu COM integration.</summary>
public partial class FileExplorerWindow : Window, INotifyPropertyChanged
{
    private readonly ControllerInput _controller = new();

    // Set while a popup this window owns (the actions menu, a rename/new-folder dialog) is
    // open - its own ControllerInput overlaps Up/Down/Accept/Back with ours, so without this
    // guard both would react to the same button press at once.
    private bool _inputBlocked;

    private string? _clipboardPath;
    private bool _clipboardIsDirectory;
    private bool _clipboardIsCut;

    public FileExplorerPaneViewModel LeftPane { get; } = new();
    public FileExplorerPaneViewModel RightPane { get; } = new();

    private FileExplorerPaneViewModel ActivePane => LeftPane.IsActive ? LeftPane : RightPane;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public FileExplorerWindow()
    {
        InitializeComponent();
        DataContext = this;

        // Folder icon, matching the launcher button that opens this window - otherwise the
        // taskbar/title bar would show WPF's generic default application icon.
        Icon = GlyphIcon.Render('', new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)), Brushes.White);

        LeftPane.IsActive = true;
        LeftPane.NavigateTo(FileExplorerPaneViewModel.ThisPc);
        RightPane.NavigateTo(FileExplorerPaneViewModel.ThisPc);

        Loaded += (_, _) => _controller.Start();
        Closed += (_, _) => _controller.Stop();

        _controller.ActionTriggered += OnControllerAction;
    }

    private void OnControllerAction(ControllerAction action)
    {
        if (_inputBlocked)
        {
            return;
        }

        switch (action)
        {
            case ControllerAction.Up: MoveSelection(-1); break;
            case ControllerAction.Down: MoveSelection(1); break;
            case ControllerAction.Accept: OpenSelected(ActivePane); break;
            case ControllerAction.Back: GoUpOrClose(ActivePane); break;
            case ControllerAction.TabPrevious: SetActivePane(LeftPane); break;
            case ControllerAction.TabNext: SetActivePane(RightPane); break;
            case ControllerAction.Backspace: OpenActionsMenu(); break;
        }
    }

    /// <summary>Square/X - the file-operations popup (Copy/Cut/Paste/Rename/New Folder/
    /// Delete), scoped to whichever pane is currently active.</summary>
    private void OpenActionsMenu()
    {
        var pane = ActivePane;
        var atRoot = pane.CurrentPath == FileExplorerPaneViewModel.ThisPc;
        var isArchive = pane.SelectedEntry is { IsDirectory: false } selected && ArchiveOperations.IsArchive(selected.FullPath);
        var isChdSource = pane.SelectedEntry is { IsDirectory: false } chdEntry && RomToolOperations.IsChdSource(chdEntry.FullPath);
        var isXisoCandidate = pane.SelectedEntry is { IsDirectory: false } xisoEntry && RomToolOperations.IsXisoCandidate(xisoEntry.FullPath);
        var menu = new FileExplorerActionsMenu(
            hasSelection: pane.SelectedEntry is not null,
            hasClipboard: _clipboardPath is not null,
            atRoot: atRoot,
            isArchive: isArchive,
            isChdSource: isChdSource,
            isXisoCandidate: isXisoCandidate)
        { Owner = this };

        _inputBlocked = true;
        var confirmed = menu.ShowDialog();
        _inputBlocked = false;

        if (confirmed == true && menu.ChosenAction is { } action)
        {
            HandleMenuAction(action, pane);
        }
    }

    private void HandleMenuAction(FileExplorerMenuAction action, FileExplorerPaneViewModel pane)
    {
        switch (action)
        {
            case FileExplorerMenuAction.Copy: SetClipboard(pane, isCut: false); break;
            case FileExplorerMenuAction.Cut: SetClipboard(pane, isCut: true); break;
            case FileExplorerMenuAction.Paste: PasteInto(pane); break;
            case FileExplorerMenuAction.Rename: RenameSelected(pane); break;
            case FileExplorerMenuAction.NewFolder: CreateNewFolder(pane); break;
            case FileExplorerMenuAction.Delete: DeleteSelected(pane); break;
            case FileExplorerMenuAction.ExtractHere: ExtractHereSelected(pane); break;
            case FileExplorerMenuAction.ExtractTo: ExtractToSelected(pane); break;
            case FileExplorerMenuAction.AddToZip: CompressSelected(pane, asSevenZip: false); break;
            case FileExplorerMenuAction.AddTo7z: CompressSelected(pane, asSevenZip: true); break;
            case FileExplorerMenuAction.CompressToChd: CompressToChd(pane); break;
            case FileExplorerMenuAction.ExtractXiso: ExtractXisoSelected(pane); break;
        }
    }

    private void CompressToChd(FileExplorerPaneViewModel pane)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        RomToolOperations.CompressToChd(entry.FullPath, this);
        pane.Refresh();
    }

    private void ExtractXisoSelected(FileExplorerPaneViewModel pane)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        RomToolOperations.ExtractXiso(entry.FullPath, this);
        pane.Refresh();
    }

    private void ExtractHereSelected(FileExplorerPaneViewModel pane)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        ArchiveOperations.ExtractHere(entry.FullPath, this);
        pane.Refresh();
    }

    private void ExtractToSelected(FileExplorerPaneViewModel pane)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Extract to" };
        _inputBlocked = true;
        var confirmed = dialog.ShowDialog(this);
        _inputBlocked = false;

        if (confirmed == true)
        {
            ArchiveOperations.ExtractTo(entry.FullPath, dialog.FolderName, this);
            LeftPane.Refresh();
            RightPane.Refresh();
        }
    }

    private void CompressSelected(FileExplorerPaneViewModel pane, bool asSevenZip)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        ArchiveOperations.AddToArchive(entry.FullPath, asSevenZip, this);
        pane.Refresh();
    }

    private void SetClipboard(FileExplorerPaneViewModel pane, bool isCut)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        _clipboardPath = entry.FullPath;
        _clipboardIsDirectory = entry.IsDirectory;
        _clipboardIsCut = isCut;
    }

    private void PasteInto(FileExplorerPaneViewModel pane)
    {
        if (_clipboardPath is null || pane.CurrentPath == FileExplorerPaneViewModel.ThisPc)
        {
            return;
        }

        var ok = _clipboardIsCut
            ? FileOperations.TryMove(_clipboardPath, pane.CurrentPath, _clipboardIsDirectory, this)
            : FileOperations.TryCopy(_clipboardPath, pane.CurrentPath, _clipboardIsDirectory, this);

        if (ok && _clipboardIsCut)
        {
            _clipboardPath = null;
        }

        LeftPane.Refresh();
        RightPane.Refresh();
    }

    private void RenameSelected(FileExplorerPaneViewModel pane)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        _inputBlocked = true;
        var dialog = new TextInputDialog("Rename", "New name:", entry.Name) { Owner = this };
        var confirmed = dialog.ShowDialog();
        _inputBlocked = false;

        if (confirmed == true && dialog.ResultText is { Length: > 0 } newName && newName != entry.Name)
        {
            FileOperations.TryRename(entry.FullPath, newName, entry.IsDirectory, this);
            pane.Refresh();
        }
    }

    private void CreateNewFolder(FileExplorerPaneViewModel pane)
    {
        if (pane.CurrentPath == FileExplorerPaneViewModel.ThisPc)
        {
            return;
        }

        _inputBlocked = true;
        var dialog = new TextInputDialog("New Folder", "Folder name:", "New folder") { Owner = this };
        var confirmed = dialog.ShowDialog();
        _inputBlocked = false;

        if (confirmed == true && dialog.ResultText is { Length: > 0 } name)
        {
            FileOperations.TryCreateFolder(pane.CurrentPath, name, this);
            pane.Refresh();
        }
    }

    private void DeleteSelected(FileExplorerPaneViewModel pane)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        FileOperations.TryDelete(entry.FullPath, entry.IsDirectory, this);
        pane.Refresh();
    }

    /// <summary>Circle/B - goes up a folder, same as everywhere else in GridShell; once
    /// there's nowhere left to go up (already at This PC), the same button closes the window
    /// instead of doing nothing, matching how Back/B closes other GridShell popups.</summary>
    private void GoUpOrClose(FileExplorerPaneViewModel pane)
    {
        if (pane.CanGoUp)
        {
            pane.GoUp();
        }
        else
        {
            Close();
        }
    }

    private void MoveSelection(int delta)
    {
        var pane = ActivePane;
        if (pane.Entries.Count == 0)
        {
            return;
        }

        var currentIndex = pane.SelectedEntry is null ? -1 : pane.Entries.IndexOf(pane.SelectedEntry);
        var newIndex = Math.Clamp(currentIndex + delta, 0, pane.Entries.Count - 1);
        pane.SelectedEntry = pane.Entries[newIndex];
    }

    private void SetActivePane(FileExplorerPaneViewModel pane)
    {
        LeftPane.IsActive = pane == LeftPane;
        RightPane.IsActive = pane == RightPane;
    }

    private static void OpenSelected(FileExplorerPaneViewModel pane)
    {
        if (pane.SelectedEntry is not { } entry)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            pane.NavigateTo(entry.FullPath);
        }
        else
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is FileExplorerPaneViewModel pane)
        {
            SetActivePane(pane);
            pane.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is FileExplorerPaneViewModel pane)
        {
            SetActivePane(pane);
            pane.GoForward();
        }
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is FileExplorerPaneViewModel pane)
        {
            SetActivePane(pane);
            pane.GoUp();
        }
    }

    /// <summary>Clicking anywhere in a pane (not just an item) makes it the active one - matches
    /// how a real dual-pane manager tracks "which side" without needing an explicit focus click
    /// on a specific row.</summary>
    private void PaneListView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListView)?.Tag is FileExplorerPaneViewModel pane)
        {
            SetActivePane(pane);

            // Right-click is the mouse-only equivalent of Square/X - without this, the actions
            // menu (and everything only reachable through it, like archive extract/compress)
            // had no mouse or keyboard path at all.
            if (e.ChangedButton == MouseButton.Right)
            {
                OpenActionsMenu();
            }
        }
    }

    private void PaneListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListView)?.Tag is FileExplorerPaneViewModel pane)
        {
            SetActivePane(pane);
            OpenSelected(pane);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_inputBlocked)
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var ctrlShift = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

        switch (e.Key)
        {
            case Key.Enter:
                OpenSelected(ActivePane);
                break;
            case Key.Back:
                GoUpOrClose(ActivePane);
                break;
            case Key.Escape:
                Close();
                break;
            case Key.F5:
                ActivePane.Refresh();
                break;
            case Key.Delete:
                DeleteSelected(ActivePane);
                break;
            case Key.F2:
                RenameSelected(ActivePane);
                break;
            case Key.N when ctrlShift:
                CreateNewFolder(ActivePane);
                break;
            case Key.C when ctrl:
                SetClipboard(ActivePane, isCut: false);
                break;
            case Key.X when ctrl:
                SetClipboard(ActivePane, isCut: true);
                break;
            case Key.V when ctrl:
                PasteInto(ActivePane);
                break;
            case Key.Apps:
            case Key.F10 when Keyboard.Modifiers == ModifierKeys.Shift:
                OpenActionsMenu();
                break;
        }
    }
}
