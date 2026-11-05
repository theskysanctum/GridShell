using System.IO;
using System.Net;
using System.Text.Json;

namespace GridShell.Core.RealDebrid;

public sealed record BridgeDownloadRequest(string Url, string? Filename, string? Referrer);

/// <summary>A tiny localhost-only HTTP listener the Chrome extension posts intercepted
/// downloads to, so they land in the Download Manager instead of Chrome's own download flow.
/// Only ever binds to 127.0.0.1 - this is a local IPC channel between the extension and this
/// process, not a real network-facing server, so it needs no auth beyond "only this machine
/// can reach it".</summary>
public sealed class DownloadBridgeServer : IDisposable
{
    public const int Port = 47821;

    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public event Action<BridgeDownloadRequest>? DownloadReceived;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => HandleRequestAsync(context), token);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            // The extension's background service worker fetches this cross-origin, from
            // whatever page triggered the download.
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/add-download")
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            var filename = root.TryGetProperty("filename", out var f) ? f.GetString() : null;
            var referrer = root.TryGetProperty("referrer", out var r) ? r.GetString() : null;

            DownloadReceived?.Invoke(new BridgeDownloadRequest(url, filename, referrer));

            context.Response.StatusCode = 200;
            context.Response.Close();
        }
        catch (Exception ex)
        {
            // Deliberately broad - this runs a subscriber's arbitrary handler
            // (DownloadReceived), so anything could go wrong in there; the listener itself
            // must never go down over it. Logged so a genuine bug is still diagnosable.
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "GridShell-DownloadBridge-errors.log"),
                    $"{DateTime.Now:O} {ex}\n\n");
            }
            catch (IOException)
            {
            }

            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch (Exception closeEx) when (closeEx is IOException or ObjectDisposedException)
            {
            }
        }
    }

    public void Dispose() => Stop();
}
