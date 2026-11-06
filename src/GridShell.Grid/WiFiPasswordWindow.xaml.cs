using System.Windows;
using System.Windows.Input;
using GridShell.Core.Interop;

namespace GridShell;

public partial class WiFiPasswordWindow : Window
{
    private string _password = "";

    public string Ssid { get; }
    public string? ResultPassword { get; private set; }

    public WiFiPasswordWindow(string ssid)
    {
        Ssid = ssid;

        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();
        this.HideWindowIcon();
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) => _password = PasswordInput.Password;

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Connect_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        ResultPassword = _password;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
