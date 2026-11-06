using System.Windows;
using GridShell.Core.Interop;

namespace GridShell;

/// <summary>Themed Yes/No (or OK-only) confirmation dialog - replaces the OS-native
/// System.Windows.MessageBox for the power menu's Sign out/Restart/Shut down confirmations and
/// the "use GridShell as the Windows shell" flow, so these follow the app's own dark/light
/// theme and accent color instead of looking like an unstyled Windows dialog.</summary>
public partial class ConfirmationWindow : Window
{
    private ConfirmationWindow(string title, string message, bool showCancel, string confirmText, string cancelText)
    {
        InitializeComponent();
        this.HideWindowIcon();
        Title = title;
        HeadingText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

        // The confirm button is deliberately NOT IsDefault - for a destructive/system action
        // (shut down, restart, replacing the shell), Enter should never accidentally trigger
        // it; Cancel/Escape is the safe default, matching the MessageBoxResult.No default the
        // plain MessageBox calls this replaces used to set explicitly.
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static bool Confirm(Window owner, string title, string message, string confirmText = "Continue", string cancelText = "Cancel")
    {
        var dialog = new ConfirmationWindow(title, message, showCancel: true, confirmText, cancelText) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    public static void Info(Window owner, string title, string message)
    {
        var dialog = new ConfirmationWindow(title, message, showCancel: false, "OK", "") { Owner = owner };
        dialog.ShowDialog();
    }
}
