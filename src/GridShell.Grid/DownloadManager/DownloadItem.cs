using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GridShell;

public enum DownloadState
{
    Resolving,
    WaitingOnTorrent,
    Downloading,
    Completed,
    Failed,
    Canceled,
}

/// <summary>One queued/active/finished download - a pasted hoster link, a pasted magnet, or a
/// URL forwarded from the Chrome extension bridge.</summary>
public class DownloadItem : INotifyPropertyChanged
{
    public required string SourceInput { get; init; }
    public readonly CancellationTokenSource Cts = new();

    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
        }
    }

    private long _totalBytes;
    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            _totalBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    private long _downloadedBytes;
    public long DownloadedBytes
    {
        get => _downloadedBytes;
        set
        {
            _downloadedBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    private long _downloadSpeedBytesPerSec;
    public long DownloadSpeedBytesPerSec
    {
        get => _downloadSpeedBytesPerSec;
        set
        {
            _downloadSpeedBytesPerSec = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpeedDisplay));
        }
    }

    public string SpeedDisplay => State == DownloadState.Downloading && DownloadSpeedBytesPerSec > 0
        ? $"{FormatBytes(DownloadSpeedBytesPerSec)}/s"
        : "";

    private DownloadState _state = DownloadState.Resolving;
    public DownloadState State
    {
        get => _state;
        set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(SpeedDisplay));
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    private string? _savedToPath;
    public string? SavedToPath
    {
        get => _savedToPath;
        set
        {
            _savedToPath = value;
            OnPropertyChanged();
        }
    }

    public double ProgressPercent => TotalBytes > 0 ? Math.Min(100.0, DownloadedBytes * 100.0 / TotalBytes) : 0;

    public string SizeDisplay => TotalBytes > 0
        ? $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)}"
        : FormatBytes(DownloadedBytes);

    public string StatusDisplay => State switch
    {
        DownloadState.Resolving => "Resolving...",
        DownloadState.WaitingOnTorrent => "Waiting on Real-Debrid...",
        DownloadState.Downloading => "Downloading",
        DownloadState.Completed => "Completed",
        DownloadState.Failed => ErrorMessage is { Length: > 0 } ? $"Failed: {ErrorMessage}" : "Failed",
        DownloadState.Canceled => "Canceled",
        _ => "",
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
