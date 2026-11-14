using System.IO;
using System.Windows;
using GridShell.Core.Downloads;
using Microsoft.Web.WebView2.Core;

namespace GridShell;

public partial class ArchiveOrgLoginWindow : Window
{
    public string? ResultCookieHeader { get; private set; }

    private readonly string _userDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "ArchiveOrgWebView2");

    public ArchiveOrgLoginWindow()
    {
        InitializeComponent();
        Loaded += ArchiveOrgLoginWindow_Loaded;
    }

    private async void ArchiveOrgLoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await WebView.EnsureCoreWebView2Async(environment);

            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            WebView.CoreWebView2.Navigate("https://archive.org/account/login");
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is WebView2RuntimeNotFoundException)
        {
            new ToastWindow("WebView2 is not available on this PC. Install the WebView2 runtime and try again.", TimeSpan.FromSeconds(6), isWarning: true).Show();
            DialogResult = false;
            Close();
        }
    }

    private async void UseSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 is null)
        {
            return;
        }

        var cookieList = await WebView.CoreWebView2.CookieManager.GetCookiesAsync("https://archive.org");
        var cookieHeader = ArchiveOrgSession.ToHeaderValue(cookieList);
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            new ToastWindow("No Archive.org session cookies were found yet. Sign in and try again.", TimeSpan.FromSeconds(5), isWarning: true).Show();
            return;
        }

        ResultCookieHeader = cookieHeader;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
