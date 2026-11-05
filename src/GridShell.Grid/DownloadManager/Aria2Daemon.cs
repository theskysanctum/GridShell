using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace GridShell;

/// <summary>Runs the bundled aria2c as a persistent background RPC daemon - the Download
/// Manager's actual file-transfer engine, giving multi-connection segmented downloads (much
/// faster than a single HTTP stream for large files) instead of hand-rolling that. Started
/// once for this process's lifetime; DownloadManagerWindow falls back to a plain direct
/// download if this never managed to start.</summary>
public sealed class Aria2Daemon : IDisposable
{
    public int Port { get; } = Random.Shared.Next(16000, 20000);
    public string Secret { get; } = Guid.NewGuid().ToString("N");
    public bool IsRunning { get; private set; }

    private Process? _process;

    public bool Start()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "Tools", "aria2c.exe");
        if (!File.Exists(exePath))
        {
            return false;
        }

        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add("--enable-rpc");
        psi.ArgumentList.Add($"--rpc-listen-port={Port}");
        psi.ArgumentList.Add($"--rpc-secret={Secret}");
        psi.ArgumentList.Add("--continue=true");
        psi.ArgumentList.Add("--max-connection-per-server=16");
        psi.ArgumentList.Add("--split=16");
        psi.ArgumentList.Add("--min-split-size=1M");
        psi.ArgumentList.Add("--allow-overwrite=true");
        psi.ArgumentList.Add("--disable-ipv6=true");
        psi.ArgumentList.Add("--console-log-level=warn");
        psi.ArgumentList.Add("--quiet=true");

        try
        {
            _process = Process.Start(psi);
            IsRunning = _process is not null;
            return IsRunning;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
