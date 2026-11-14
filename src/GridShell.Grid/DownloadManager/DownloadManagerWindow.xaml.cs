using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GridShell.Core.Downloads;
using GridShell.Core.Interop;
using GridShell.Core.RealDebrid;
using GridShell.Core.Settings;
using GridShell.Core.Store;
using GridShell.Core.Tiles;

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

    public ObservableCollection<EmulationPlatform> RomPlatforms { get; } = new();

    private bool _saveAsRom;
    public bool SaveAsRom
    {
        get => _saveAsRom;
        set
        {
            _saveAsRom = value;
            OnPropertyChanged();
        }
    }

    private EmulationPlatform? _selectedRomPlatform;
    public EmulationPlatform? SelectedRomPlatform
    {
        get => _selectedRomPlatform;
        set
        {
            _selectedRomPlatform = value;
            OnPropertyChanged();
            RefreshRomFolderOptions();
        }
    }

    public ObservableCollection<string> RomFolderOptions { get; } = new();

    private string? _selectedRomFolder;
    public string? SelectedRomFolder
    {
        get => _selectedRomFolder;
        set
        {
            _selectedRomFolder = value;
            OnPropertyChanged();
        }
    }

    private void RefreshRomFolderOptions()
    {
        RomFolderOptions.Clear();
        if (SelectedRomPlatform is { } platform)
        {
            var roots = new[] { EmulationPlatforms.RomsRootPath }.Concat(_settings.ExtraRomPaths);
            foreach (var root in roots)
            {
                RomFolderOptions.Add(Path.Combine(root, platform.Slug));
            }
        }

        SelectedRomFolder = RomFolderOptions.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public DownloadManagerWindow()
    {
        InitializeComponent();
        DataContext = this;

        Icon = GlyphIcon.Render('', new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)), Brushes.White);

        _settings = AppSettingsStore.Load();

        foreach (var platform in EmulationPlatforms.All)
        {
            if (_settings.EmulatorConfigs.TryGetValue(platform.Slug, out var config) &&
                !string.IsNullOrWhiteSpace(config.EmulatorPath))
            {
                RomPlatforms.Add(platform);
            }
        }

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
        var romFolder = SaveAsRom ? SelectedRomFolder : null;
        AddDownload(input, null, romFolder);
    }

    public void AddDownload(string input, string? suggestedName) => AddDownload(input, suggestedName, null);

    public void AddDownload(string input, string? suggestedName, string? destinationFolder) =>
        AddDownload(new List<string> { input }, suggestedName, destinationFolder);

    public void AddDownload(List<string> mirrorUrls, string? suggestedName, string? destinationFolder)
    {
        var item = new DownloadItem
        {
            SourceInput = mirrorUrls[0],
            FallbackUrls = mirrorUrls.Skip(1).ToList(),
            Name = suggestedName ?? mirrorUrls[0],
        };
        Downloads.Add(item);
        SelectedDownload = item;
        _ = ProcessAsync(item, destinationFolder);
    }

    private async Task ProcessAsync(DownloadItem item, string? destinationFolder)
    {
        var folder = destinationFolder ?? (string.IsNullOrWhiteSpace(_settings.DownloadManagerFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            : _settings.DownloadManagerFolder);
        var extractArchive = destinationFolder is not null;

        try
        {
            Directory.CreateDirectory(folder);

            if (item.SourceInput.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessMagnetAsync(item, folder, extractArchive);
            }
            else
            {
                await ProcessLinkWithFallbackAsync(item, folder, extractArchive);
            }

            item.State = DownloadState.Completed;
            new ToastWindow($"{RomTitleCleaner.CleanFileName(item.Name)} downloaded.", TimeSpan.FromSeconds(5)).Show();
        }
        catch (OperationCanceledException)
        {
            item.State = DownloadState.Canceled;
        }
        catch (Exception ex)
        {
            item.State = DownloadState.Failed;
            item.ErrorMessage = ex.Message;
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "GridShell-DownloadBridge-errors.log"),
                $"{DateTime.Now:O} ProcessAsync failed for '{item.SourceInput}': {ex}\n\n");
        }
    }

    private async Task ProcessLinkWithFallbackAsync(DownloadItem item, string folder, bool extractArchive)
    {
        var candidates = new List<string> { item.SourceInput }.Concat(item.FallbackUrls).ToList();
        Exception? lastError = null;

        for (var i = 0; i < candidates.Count; i++)
        {
            try
            {
                await ProcessLinkAsync(item, folder, extractArchive, candidates[i]);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                lastError = ex;
                if (i < candidates.Count - 1)
                {
                    item.DownloadedBytes = 0;
                    item.TotalBytes = 0;
                }
            }
        }

        throw lastError ?? new IOException("All mirrors failed.");
    }

    private async Task ProcessLinkAsync(DownloadItem item, string folder, bool extractArchive, string sourceUrl)
    {
        item.State = DownloadState.Resolving;

        var downloadUrl = sourceUrl;
        var fileName = (item.Name != item.SourceInput ? item.Name : null)
            ?? TryGetFileNameFromUrl(sourceUrl)
            ?? "download";
        long totalBytes = 0;

        if (!string.IsNullOrWhiteSpace(_settings.RealDebridApiToken))
        {
            var unrestricted = await RealDebridClient.UnrestrictLinkAsync(_settings.RealDebridApiToken, sourceUrl);
            if (unrestricted is not null)
            {
                downloadUrl = unrestricted.DownloadUrl;
                fileName = unrestricted.Filename;
                totalBytes = unrestricted.Filesize;
            }
        }

        item.Name = fileName;
        item.TotalBytes = totalBytes;
        item.State = DownloadState.Downloading;
        await DownloadFileAsync(item, downloadUrl, folder, fileName, extractArchive);
    }

    private async Task ProcessMagnetAsync(DownloadItem item, string folder, bool extractArchive)
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
            await DownloadFileAsync(item, unrestricted.DownloadUrl, folder, unrestricted.Filename, extractArchive);
        }
    }

    private async Task DownloadFileAsync(DownloadItem item, string url, string folder, string fileName, bool extractArchive)
    {
        var sanitizedName = SanitizeFileName(fileName);
        var app = (App)Application.Current;

        if (app.Aria2.IsRunning)
        {
            var gid = await Aria2Client.AddUriAsync(app.Aria2.Port, app.Aria2.Secret, url, folder, sanitizedName, _settings.DownloadCookies);
            if (gid is not null)
            {
                await TrackAria2DownloadAsync(item, app.Aria2, gid, folder, sanitizedName);
                if (extractArchive)
                {
                    await ExtractIntoDestinationAsync(item, folder, sanitizedName);
                }

                return;
            }
        }

        await DownloadFileDirectAsync(item, url, folder, sanitizedName, _settings.DownloadCookies);
        if (extractArchive)
        {
            await ExtractIntoDestinationAsync(item, folder, sanitizedName);
        }
    }

    private async Task ExtractIntoDestinationAsync(DownloadItem item, string folder, string fileName)
    {
        var archivePath = Path.Combine(folder, fileName);
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archivePath, folder, overwriteFiles: true);
            }
            else
            {
                var error = await SevenZipExtractor.ExtractAsync(archivePath, folder);
                if (error is not null)
                {
                    item.ErrorMessage = $"Downloaded, but couldn't extract: {error}";
                    return;
                }
            }

            File.Delete(archivePath);
            item.SavedToPath = folder;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            item.ErrorMessage = $"Downloaded, but couldn't extract: {ex.Message}";
        }
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

    private async Task DownloadFileDirectAsync(DownloadItem item, string url, string folder, string fileName, string? cookies = null)
    {
        var destPath = Path.Combine(folder, fileName);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(cookies))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
        }

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, item.Cts.Token);
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
