using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GridShell.Core.Downloads;
using GridShell.Core.Interop;
using GridShell.Core.RealDebrid;
using GridShell.Core.Settings;

namespace GridShell;

/// <summary>GridShell's download manager - paste a hoster link or a magnet, both resolved
/// through Real-Debrid (when a token is set in Settings) and downloaded locally with progress.
/// Also receives downloads forwarded from the bundled Chrome extension via
/// DownloadBridgeServer, so a browser download lands here instead of Chrome's own flow.</summary>
public partial class DownloadManagerWindow : Window, INotifyPropertyChanged
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(6) };

    private readonly ControllerInput _controller = new();
    private readonly AppSettings _settings;

    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    private DownloadItem? _selectedDownload;
    public DownloadItem? SelectedDownload
    {
        get => _selectedDownload;
        set
        {
            _selectedDownload = value;
            OnPropertyChanged();
        }
    }

    private string _realDebridStatusText = "Real-Debrid: checking...";
    public string RealDebridStatusText
    {
        get => _realDebridStatusText;
        set
        {
            _realDebridStatusText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public DownloadManagerWindow()
    {
        InitializeComponent();
        DataContext = this;

        // Same "download" glyph as the launcher button that opens this window, rendered to a
        // bitmap since a code-behind FontFamily reference needs an explicit pack-URI base to
        // resolve at all (a plain relative string only resolves automatically inside XAML).
        Icon = GlyphIcon.Render('', new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)), Brushes.White);

        _settings = AppSettingsStore.Load();

        // This window is a reused singleton for the process's whole lifetime (App.
        // ShowDownloadManager), same as MainWindow - a real Close() would destroy the Downloads
        // queue and orphan any in-flight downloads with no UI left to see or cancel them from.
        // Closing instead hides it, same convention MainWindow already uses.
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                _controller.Start();
                _ = RefreshRealDebridStatusAsync();
            }
            else
            {
                _controller.Stop();
            }
        };

        _controller.ActionTriggered += OnControllerAction;
    }

    private async Task RefreshRealDebridStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.RealDebridApiToken))
        {
            RealDebridStatusText = "Real-Debrid: no API token set (Settings > Downloads) - links download directly instead.";
            return;
        }

        var user = await RealDebridClient.GetUserAsync(_settings.RealDebridApiToken);
        RealDebridStatusText = user is null
            ? "Real-Debrid: couldn't connect - check your API token in Settings."
            : $"Real-Debrid: connected as {user.Username} ({user.Type})";
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => AddFromInputBox();

    private void LinkInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddFromInputBox();
        }
    }

    private void AddFromInputBox()
    {
        var input = LinkInputBox.Text.Trim();
        if (input.Length == 0)
        {
            return;
        }

        LinkInputBox.Text = "";
        AddDownload(input, null);
    }

    public void AddDownload(string input, string? suggestedName)
    {
        var item = new DownloadItem { SourceInput = input, Name = suggestedName ?? input };
        Downloads.Add(item);
        SelectedDownload = item;
        _ = ProcessAsync(item);
    }

    private async Task ProcessAsync(DownloadItem item)
    {
        var folder = string.IsNullOrWhiteSpace(_settings.DownloadManagerFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            : _settings.DownloadManagerFolder;

        try
        {
            Directory.CreateDirectory(folder);

            if (item.SourceInput.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessMagnetAsync(item, folder);
            }
            else
            {
                await ProcessLinkAsync(item, folder);
            }

            item.State = DownloadState.Completed;
        }
        catch (OperationCanceledException)
        {
            item.State = DownloadState.Canceled;
        }
        catch (Exception ex)
        {
            // Deliberately broad, not just HttpRequestException/IOException - any unexpected
            // failure here should leave the item visibly Failed with a message, not silently
            // stuck "Resolving" forever as an unobserved background-task exception.
            item.State = DownloadState.Failed;
            item.ErrorMessage = ex.Message;
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "GridShell-DownloadBridge-errors.log"),
                $"{DateTime.Now:O} ProcessAsync failed for '{item.SourceInput}': {ex}\n\n");
        }
    }

    private async Task ProcessLinkAsync(DownloadItem item, string folder)
    {
        item.State = DownloadState.Resolving;

        var downloadUrl = item.SourceInput;

        // AddDownload already set item.Name to whatever the browser extension suggested; only
        // fall back to guessing from the URL's path when there was no suggestion (item.Name
        // still equals the raw pasted input).
        var fileName = (item.Name != item.SourceInput ? item.Name : null)
            ?? TryGetFileNameFromUrl(item.SourceInput)
            ?? "download";
        long totalBytes = 0;

        if (!string.IsNullOrWhiteSpace(_settings.RealDebridApiToken))
        {
            var unrestricted = await RealDebridClient.UnrestrictLinkAsync(_settings.RealDebridApiToken, item.SourceInput);
            if (unrestricted is not null)
            {
                downloadUrl = unrestricted.DownloadUrl;
                fileName = unrestricted.Filename;
                totalBytes = unrestricted.Filesize;
            }
            // Real-Debrid not recognizing the link (null) isn't fatal - it just means this
            // wasn't a restricted hoster link, so fall through and download it as-is.
        }

        item.Name = fileName;
        item.TotalBytes = totalBytes;
        item.State = DownloadState.Downloading;
        await DownloadFileAsync(item, downloadUrl, folder, fileName);
    }

    private async Task ProcessMagnetAsync(DownloadItem item, string folder)
    {
        if (string.IsNullOrWhiteSpace(_settings.RealDebridApiToken))
        {
            item.State = DownloadState.Failed;
            item.ErrorMessage = "Magnet links need a Real-Debrid API token (Settings > Downloads).";
            return;
        }

        item.State = DownloadState.Resolving;
        var torrentId = await RealDebridClient.AddMagnetAsync(_settings.RealDebridApiToken, item.SourceInput);
        if (torrentId is null)
        {
            item.State = DownloadState.Failed;
            item.ErrorMessage = "Real-Debrid couldn't add this magnet.";
            return;
        }

        item.State = DownloadState.WaitingOnTorrent;

        TorrentInfo? info;
        var filesSelected = false;
        while (true)
        {
            item.Cts.Token.ThrowIfCancellationRequested();

            info = await RealDebridClient.GetTorrentInfoAsync(_settings.RealDebridApiToken, torrentId);
            if (info is null)
            {
                item.State = DownloadState.Failed;
                item.ErrorMessage = "Lost track of the torrent on Real-Debrid.";
                return;
            }

            if (info.Filename is { Length: > 0 })
            {
                item.Name = info.Filename;
            }

            if (info.Status == "waiting_files_selection" && !filesSelected)
            {
                filesSelected = await RealDebridClient.SelectAllFilesAsync(_settings.RealDebridApiToken, torrentId);
            }
            else if (info.Status == "downloaded")
            {
                break;
            }
            else if (info.Status is "error" or "virus" or "dead" or "magnet_error")
            {
                item.State = DownloadState.Failed;
                item.ErrorMessage = $"Real-Debrid reported: {info.Status}";
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), item.Cts.Token);
        }

        item.State = DownloadState.Downloading;
        foreach (var link in info.Links)
        {
            var unrestricted = await RealDebridClient.UnrestrictLinkAsync(_settings.RealDebridApiToken, link);
            if (unrestricted is null)
            {
                continue;
            }

            item.TotalBytes += unrestricted.Filesize;
            await DownloadFileAsync(item, unrestricted.DownloadUrl, folder, unrestricted.Filename);
        }
    }

    /// <summary>Downloads via the bundled aria2c (multi-connection, much faster for large
    /// files) when its RPC daemon is up; falls back to a plain single-connection download
    /// otherwise, so the feature still works if the bundled binary failed to start.</summary>
    private async Task DownloadFileAsync(DownloadItem item, string url, string folder, string fileName)
    {
        var sanitizedName = SanitizeFileName(fileName);
        var app = (App)Application.Current;

        if (app.Aria2.IsRunning)
        {
            var gid = await Aria2Client.AddUriAsync(app.Aria2.Port, app.Aria2.Secret, url, folder, sanitizedName);
            if (gid is not null)
            {
                await TrackAria2DownloadAsync(item, app.Aria2, gid, folder, sanitizedName);
                return;
            }
        }

        await DownloadFileDirectAsync(item, url, folder, sanitizedName);
    }

    private async Task TrackAria2DownloadAsync(DownloadItem item, Aria2Daemon aria2, string gid, string folder, string fileName)
    {
        try
        {
            while (true)
            {
                item.Cts.Token.ThrowIfCancellationRequested();

                var status = await Aria2Client.TellStatusAsync(aria2.Port, aria2.Secret, gid);
                if (status is null)
                {
                    // Lost contact with the daemon mid-download - can't track progress any
                    // further, but the file on disk (if any) is aria2's responsibility now.
                    throw new IOException("Lost contact with the aria2 download engine.");
                }

                if (status.TotalLength > 0)
                {
                    item.TotalBytes = status.TotalLength;
                }

                item.DownloadedBytes = status.CompletedLength;
                item.DownloadSpeedBytesPerSec = status.DownloadSpeed;

                if (status.Status == "complete")
                {
                    item.SavedToPath = Path.Combine(folder, fileName);
                    return;
                }

                if (status.Status is "error" or "removed")
                {
                    throw new IOException(status.ErrorMessage is { Length: > 0 }
                        ? status.ErrorMessage
                        : $"aria2 reported: {status.Status}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(400), item.Cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            await Aria2Client.ForceRemoveAsync(aria2.Port, aria2.Secret, gid);
            throw;
        }
    }

    private async Task DownloadFileDirectAsync(DownloadItem item, string url, string folder, string fileName)
    {
        var destPath = Path.Combine(folder, fileName);

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, item.Cts.Token);
        response.EnsureSuccessStatusCode();
        if (item.TotalBytes == 0)
        {
            item.TotalBytes = response.Content.Headers.ContentLength ?? 0;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(item.Cts.Token);
        await using var fileStream = File.Create(destPath);

        var buffer = new byte[81920];
        var downloaded = item.DownloadedBytes;
        var lastUiUpdate = DateTime.UtcNow;
        int read;
        while ((read = await contentStream.ReadAsync(buffer, item.Cts.Token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), item.Cts.Token);
            downloaded += read;

            // Setting the bound property on every ~80KB chunk - easily hundreds of times a
            // second on a fast connection - made the whole queue list visibly stutter.
            // Throttling to 10x/second keeps progress smooth without looking stalled.
            var now = DateTime.UtcNow;
            if (now - lastUiUpdate >= TimeSpan.FromMilliseconds(100))
            {
                item.DownloadedBytes = downloaded;
                lastUiUpdate = now;
            }
        }

        item.DownloadedBytes = downloaded;
        item.SavedToPath = destPath;
    }

    private static string? TryGetFileNameFromUrl(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).LocalPath);
            return string.IsNullOrWhiteSpace(name) ? null : Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }

    private void OnControllerAction(ControllerAction action)
    {
        switch (action)
        {
            case ControllerAction.Up: MoveSelection(-1); break;
            case ControllerAction.Down: MoveSelection(1); break;
            case ControllerAction.Accept: OpenSelectedFolder(); break;
            case ControllerAction.Backspace: CancelOrRemoveSelected(); break;
            case ControllerAction.Back:
                Close();
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (Downloads.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedDownload is null ? -1 : Downloads.IndexOf(SelectedDownload);
        var newIndex = Math.Clamp(currentIndex + delta, 0, Downloads.Count - 1);
        SelectedDownload = Downloads[newIndex];
    }

    private void OpenSelectedFolder()
    {
        if (SelectedDownload is { SavedToPath: { } path } && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
    }

    private void CancelOrRemoveSelected()
    {
        if (SelectedDownload is not { } item)
        {
            return;
        }

        if (item.State is DownloadState.Downloading or DownloadState.Resolving or DownloadState.WaitingOnTorrent)
        {
            item.Cts.Cancel();
        }
        else
        {
            Downloads.Remove(item);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Delete:
                CancelOrRemoveSelected();
                break;
            case Key.Enter when !LinkInputBox.IsFocused:
                OpenSelectedFolder();
                break;
        }
    }
}
