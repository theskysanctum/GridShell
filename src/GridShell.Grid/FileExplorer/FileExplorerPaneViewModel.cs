using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using GridShell.Core.Interop;

namespace GridShell;

public enum FileSortMode
{
    Name,
    Size,
    DateModified,
    Type,
}

/// <summary>One pane's browsing state - current folder (or the virtual "This PC" drive list),
/// its entries, selection, and back/forward history. Two of these make up a dual-pane window,
/// each independently navigable.</summary>
public class FileExplorerPaneViewModel : INotifyPropertyChanged
{
    /// <summary>Sentinel for the virtual "This PC" root (lists drives, not a real folder) - null
    /// would also work, but an explicit marker reads clearer at every call site than a null
    /// check standing in for "the drive list".</summary>
    public const string ThisPc = "";

    private readonly List<string> _backStack = new();
    private readonly List<string> _forwardStack = new();

    public ObservableCollection<FileEntry> Entries { get; } = new();

    private string _currentPath = ThisPc;
    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (_currentPath != value)
            {
                _currentPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayPath));
                OnPropertyChanged(nameof(CanGoUp));
            }
        }
    }

    public string DisplayPath => CurrentPath == ThisPc ? "This PC" : CurrentPath;

    public bool CanGoUp => CurrentPath != ThisPc;
    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;

    private FileEntry? _selectedEntry;
    public FileEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (_selectedEntry != null)
            {
                _selectedEntry.IsSelected = false;
            }

            _selectedEntry = value;
            if (_selectedEntry != null)
            {
                _selectedEntry.IsSelected = true;
            }

            OnPropertyChanged();
        }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public FileSortMode SortMode { get; set; } = FileSortMode.Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Navigates to a new location, pushing the current one onto the back stack and
    /// clearing forward history - the same rule any browser-style history uses.</summary>
    public void NavigateTo(string path)
    {
        if (path != CurrentPath)
        {
            _backStack.Add(CurrentPath);
            _forwardStack.Clear();
        }

        LoadPath(path);
    }

    public void GoUp()
    {
        if (!CanGoUp)
        {
            return;
        }

        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar));
        NavigateTo(string.IsNullOrEmpty(parent) ? ThisPc : parent);
    }

    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        var target = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        _forwardStack.Add(CurrentPath);
        LoadPath(target);
    }

    public void GoForward()
    {
        if (!CanGoForward)
        {
            return;
        }

        var target = _forwardStack[^1];
        _forwardStack.RemoveAt(_forwardStack.Count - 1);
        _backStack.Add(CurrentPath);
        LoadPath(target);
    }

    public void Refresh() => LoadPath(CurrentPath);

    private void LoadPath(string path)
    {
        CurrentPath = path;
        Entries.Clear();

        var rawEntries = path == ThisPc ? ListDrives() : ListFolder(path);
        foreach (var entry in Sort(rawEntries))
        {
            Entries.Add(entry);
            _ = LoadIconAsync(entry);
        }

        SelectedEntry = Entries.FirstOrDefault();
    }

    private static async Task LoadIconAsync(FileEntry entry)
    {
        entry.Icon = await Task.Run(() => ShellIcons.GetIcon(entry.FullPath));
    }

    private static IEnumerable<FileEntry> ListDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FileEntry
            {
                Name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.VolumeLabel} ({d.Name})",
                FullPath = d.Name,
                IsDirectory = true,
                IsDrive = true,
            });

    private static IEnumerable<FileEntry> ListFolder(string path)
    {
        var entries = new List<FileEntry>();

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var info = new DirectoryInfo(dir);
                entries.Add(new FileEntry
                {
                    Name = info.Name,
                    FullPath = dir,
                    IsDirectory = true,
                    IsDrive = false,
                    DateModified = SafeLastWriteTime(info),
                });
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                var info = new FileInfo(file);
                entries.Add(new FileEntry
                {
                    Name = info.Name,
                    FullPath = file,
                    IsDirectory = false,
                    IsDrive = false,
                    SizeBytes = SafeLength(info),
                    DateModified = SafeLastWriteTime(info),
                });
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A folder we can't enumerate (permissions, a disconnected network path) just shows
            // empty rather than crashing the whole pane.
        }

        return entries;
    }

    private static DateTime? SafeLastWriteTime(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTime;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static long SafeLength(FileInfo info)
    {
        try
        {
            return info.Length;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return 0;
        }
    }

    private IEnumerable<FileEntry> Sort(IEnumerable<FileEntry> entries)
    {
        var ordered = SortMode switch
        {
            FileSortMode.Size => entries.OrderBy(e => e.SizeBytes),
            FileSortMode.DateModified => entries.OrderBy(e => e.DateModified),
            FileSortMode.Type => entries.OrderBy(e => Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase),
            _ => entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };

        // Folders/drives always sort before files, regardless of sort mode - matches Explorer's
        // own default behavior.
        return ordered.OrderByDescending(e => e.IsDirectory);
    }
}
