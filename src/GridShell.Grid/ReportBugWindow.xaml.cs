using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using GridShell.Core.Interop;

namespace GridShell;

/// <summary>Builds a pre-filled GitHub issue (title/body via query string, same way IGDB's own
/// OAuth-less flows work) rather than sending anything itself - no backend to build/host for a
/// project this size, and the user still reviews and clicks Submit on GitHub's own page before
/// anything is actually posted. Shares GridShell-errors.log's own path with
/// UnhandledExceptionLogger (not exposed from there, just the same plain
/// %TEMP%\GridShell-errors.log convention every other ad-hoc log path in this app already
/// uses).</summary>
public partial class ReportBugWindow : Window
{
    private const string RepoUrl = "https://github.com/theskysanctum/gridshell";
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "GridShell-errors.log");

    public ReportBugWindow()
    {
        InitializeComponent();
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();
        this.HideWindowIcon();
        Loaded += (_, _) => DescriptionBox.Focus();
    }

    private void OpenIssueButton_Click(object sender, RoutedEventArgs e)
    {
        var body = BuildBody(DescriptionBox.Text.Trim(), IncludeLogCheckBox.IsChecked == true);
        var url = $"{RepoUrl}/issues/new?title={Uri.EscapeDataString("Bug report")}&body={Uri.EscapeDataString(body)}";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            new ToastWindow("Couldn't open the browser - no default browser is set.", TimeSpan.FromSeconds(5), isWarning: true).Show();
            return;
        }

        DialogResult = true;
        Close();
    }

    private static string BuildBody(string description, bool includeLog)
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        var sb = new StringBuilder();
        sb.AppendLine("## What happened");
        sb.AppendLine(description.Length > 0 ? description : "_(describe what you were doing and what went wrong)_");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine($"- GridShell version: {version}");
        sb.AppendLine($"- OS: {Environment.OSVersion.VersionString}");

        if (includeLog && TryReadRecentLog() is { } logTail)
        {
            sb.AppendLine();
            sb.AppendLine("## Recent errors (GridShell-errors.log)");
            sb.AppendLine("```");
            sb.AppendLine(logTail);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    /// <summary>The log's own last ~1500 characters, not the whole file - GridShell-errors.log
    /// can grow to hundreds of KB over time (every DispatcherUnhandledException ever hit gets
    /// appended, never trimmed), and a GitHub issue URL this long risks the browser or GitHub
    /// itself silently truncating it well before the part that actually matters: the most
    /// recent entry.</summary>
    private static string? TryReadRecentLog()
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return null;
            }

            var text = File.ReadAllText(LogPath);
            return text.Length > 1500 ? text[^1500..] : text;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
