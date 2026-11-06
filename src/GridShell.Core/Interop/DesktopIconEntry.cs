using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GridShell.Core.Interop;

/// <summary>One entry in the Desktop-Icons widget - a minimal duplicate of GridShell.Grid's
/// FileEntry/FileExplorerPaneViewModel, which can't be reused directly since that project isn't
/// referenced by GridShell (the taskbar process, where this widget lives) - the two WPF
/// projects are intentionally separate. Icon is loaded asynchronously after the entry is
/// created (same lazy-load pattern as FileEntry.Icon), hence INotifyPropertyChanged.</summary>
public sealed class DesktopIconEntry : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class DesktopIconLoader
{
    /// <summary>A static snapshot of %USERPROFILE%\Desktop - directories first, then
    /// alphabetical by name (matches FileExplorerPaneViewModel.Sort's own convention), hidden/
    /// system entries (desktop.ini etc.) filtered out to match what a real desktop shows.
    /// No live file-system watching for v1 - re-adding the widget already does a full
    /// recreate.</summary>
    public static List<DesktopIconEntry> ListDesktop()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var entries = new List<DesktopIconEntry>();

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(desktopPath))
            {
                var info = new DirectoryInfo(dir);
                if (IsHiddenOrSystem(info.Attributes))
                {
                    continue;
                }

                entries.Add(new DesktopIconEntry { Name = info.Name, FullPath = dir, IsDirectory = true });
            }

            foreach (var file in Directory.EnumerateFiles(desktopPath))
            {
                var info = new FileInfo(file);
                if (IsHiddenOrSystem(info.Attributes))
                {
                    continue;
                }

                entries.Add(new DesktopIconEntry { Name = info.Name, FullPath = file, IsDirectory = false });
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A desktop folder we can't enumerate just shows empty rather than crashing.
        }

        return entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsHiddenOrSystem(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);

    public static Task LoadIconAsync(DesktopIconEntry entry) =>
        Task.Run(() => entry.Icon = ShellIcons.GetIcon(entry.FullPath));
}
