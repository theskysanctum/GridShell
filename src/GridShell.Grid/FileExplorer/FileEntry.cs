using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GridShell;

/// <summary>One row in a file explorer pane - a file, folder, or drive. Icon is loaded
/// asynchronously after the entry is created (same lazy-load pattern as Tile.Icon), since
/// extracting a real shell icon per row would otherwise block the UI thread while a folder with
/// many items populates.</summary>
public class FileEntry : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required bool IsDrive { get; init; }
    public long SizeBytes { get; init; }
    public DateTime? DateModified { get; init; }

    public string SizeDisplay => IsDirectory ? "" : FormatSize(SizeBytes);
    public string DateDisplay => DateModified?.ToString("g") ?? "";

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{(long)size} {units[unitIndex]}" : $"{size:F1} {units[unitIndex]}";
    }

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

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
