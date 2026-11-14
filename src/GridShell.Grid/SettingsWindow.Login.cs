using System.Windows;

namespace GridShell;

public partial class SettingsWindow : Window
{
    private void ArchiveOrgLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ArchiveOrgLoginWindow { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResultCookieHeader))
        {
            return;
        }

        DownloadCookies = dialog.ResultCookieHeader;
        new ToastWindow("Archive.org session imported.", TimeSpan.FromSeconds(4)).Show();
    }

    private void ArchiveOrgClearButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadCookies = null;
        new ToastWindow("Archive.org session cleared.", TimeSpan.FromSeconds(4)).Show();
    }
}
