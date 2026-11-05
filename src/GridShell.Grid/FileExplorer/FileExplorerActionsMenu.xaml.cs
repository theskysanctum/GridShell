using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GridShell.Core.Interop;

namespace GridShell;

public enum FileExplorerMenuAction
{
    Copy,
    Cut,
    Paste,
    Rename,
    NewFolder,
    Delete,
    ExtractHere,
    ExtractTo,
    AddToZip,
    AddTo7z,
    CompressToChd,
    ExtractXiso,
}

/// <summary>Start/Menu's file-operations popup inside the File Explorer - Copy/Cut/Paste/
/// Rename/New Folder/Delete, navigable by D-pad/stick (Up/Down), Accept (A) to confirm, Back
/// (B)/Escape to cancel. Also works with mouse/keyboard alone. Uses its own ControllerInput
/// rather than the owning FileExplorerWindow's - the caller is responsible for not acting on
/// Up/Down/Accept/Back itself (the actions overlap with file-list navigation) while this is
/// open, via its own guard flag.</summary>
public partial class FileExplorerActionsMenu : Window
{
    // Reads the live AccentBrush resource rather than hardcoding the default blue, so this
    // popup's selection highlight follows the user's chosen accent color like everything else.
    private static Brush SelectedBrush =>
        Application.Current.Resources["AccentBrush"] as Brush ?? Brushes.DodgerBlue;
    private static readonly Brush DisabledBrush = (Brush)new BrushConverter().ConvertFromString("#777")!;

    private readonly ControllerInput _controller = new(NavigationStick.None);
    private Border[] _items = Array.Empty<Border>();
    private bool[] _enabled = Array.Empty<bool>();
    private int _selectedIndex = -1;
    private bool _isClosing;

    // This popup gets its own fresh ControllerInput, so if the physical Square/X press that
    // opened it is still held when that instance starts polling, it reads as a brand new
    // press and would close the popup the instant it opens. Ignoring Backspace for a brief
    // window after Loaded gives the user time to actually release the button first.
    private DateTime _backspaceArmedAt = DateTime.MaxValue;
    private static readonly TimeSpan BackspaceArmDelay = TimeSpan.FromMilliseconds(350);

    public FileExplorerMenuAction? ChosenAction { get; private set; }

    public FileExplorerActionsMenu(bool hasSelection, bool hasClipboard, bool atRoot, bool isArchive,
        bool isChdSource, bool isXisoCandidate)
    {
        InitializeComponent();

        _items = new[]
        {
            CopyItem, CutItem, PasteItem, RenameItem, NewFolderItem, DeleteItem,
            ExtractHereItem, ExtractToItem, AddToZipItem, AddTo7zItem,
            CompressToChdItem, ExtractXisoItem,
        };
        _enabled = new[]
        {
            hasSelection, hasSelection, hasClipboard && !atRoot, hasSelection, !atRoot, hasSelection,
            hasSelection && isArchive, hasSelection && isArchive, hasSelection, hasSelection,
            hasSelection && isChdSource, hasSelection && isXisoCandidate,
        };

        for (var i = 0; i < _items.Length; i++)
        {
            if (_enabled[i])
            {
                continue;
            }

            foreach (var child in ((StackPanel)_items[i].Child).Children)
            {
                if (child is TextBlock text)
                {
                    text.Foreground = DisabledBrush;
                }
            }
        }

        _selectedIndex = Array.IndexOf(_enabled, true);
        UpdateSelection();

        _controller.ActionTriggered += OnControllerAction;
        Closed += (_, _) =>
        {
            _controller.ActionTriggered -= OnControllerAction;
            _controller.Stop();
        };

        Loaded += (_, _) =>
        {
            _controller.Start();
            _backspaceArmedAt = DateTime.UtcNow + BackspaceArmDelay;
            Activate();
            Focus();
        };
    }

    private void OnControllerAction(ControllerAction action)
    {
        switch (action)
        {
            case ControllerAction.Up:
                Move(-1);
                break;
            case ControllerAction.Down:
                Move(1);
                break;
            case ControllerAction.Accept:
                Confirm(_selectedIndex);
                break;
            case ControllerAction.Back:
                CloseMenu();
                break;
            case ControllerAction.Backspace when DateTime.UtcNow >= _backspaceArmedAt:
                // Square/X also closes this - same button that opened it, so it reads as a
                // toggle rather than needing a different button to back out.
                CloseMenu();
                break;
        }
    }

    private void Move(int delta)
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        var index = _selectedIndex;
        for (var step = 0; step < _items.Length; step++)
        {
            index = (index + delta + _items.Length) % _items.Length;
            if (_enabled[index])
            {
                _selectedIndex = index;
                break;
            }
        }

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        for (var i = 0; i < _items.Length; i++)
        {
            _items[i].Background = i == _selectedIndex ? SelectedBrush : Brushes.Transparent;
        }
    }

    private void Confirm(int index)
    {
        if (index < 0 || !_enabled[index])
        {
            return;
        }

        ChosenAction = (FileExplorerMenuAction)index;
        CloseMenu(true);
    }

    private void CloseMenu(bool? result = null)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        if (result.HasValue)
        {
            DialogResult = result;
        }
        else
        {
            Close();
        }
    }

    private void CopyItem_Click(object sender, MouseButtonEventArgs e) => Confirm(0);
    private void CutItem_Click(object sender, MouseButtonEventArgs e) => Confirm(1);
    private void PasteItem_Click(object sender, MouseButtonEventArgs e) => Confirm(2);
    private void RenameItem_Click(object sender, MouseButtonEventArgs e) => Confirm(3);
    private void NewFolderItem_Click(object sender, MouseButtonEventArgs e) => Confirm(4);
    private void DeleteItem_Click(object sender, MouseButtonEventArgs e) => Confirm(5);
    private void ExtractHereItem_Click(object sender, MouseButtonEventArgs e) => Confirm(6);
    private void ExtractToItem_Click(object sender, MouseButtonEventArgs e) => Confirm(7);
    private void AddToZipItem_Click(object sender, MouseButtonEventArgs e) => Confirm(8);
    private void AddTo7zItem_Click(object sender, MouseButtonEventArgs e) => Confirm(9);
    private void CompressToChdItem_Click(object sender, MouseButtonEventArgs e) => Confirm(10);
    private void ExtractXisoItem_Click(object sender, MouseButtonEventArgs e) => Confirm(11);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Move(-1);
                break;
            case Key.Down:
                Move(1);
                break;
            case Key.Enter:
                Confirm(_selectedIndex);
                break;
            case Key.Escape:
                CloseMenu();
                break;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e) => CloseMenu();
}
