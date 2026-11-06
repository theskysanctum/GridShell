using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using GridShell.Core.Interop;
using GridShell.Core.Settings;

namespace GridShell;

/// <summary>Small modal for Kiosk Mode's PIN - same role as AddTileWindow/AddWidgetWindow
/// (separate dialog, ShowDialog, result read after). Two modes:
/// - Set (parameterless constructor): choose a new PIN, confirmed by re-entry; exposes
///   ResultPinHash (via PinHasher.Hash) on success.
/// - Verify (constructor takes the existing hash): enter the current PIN; a wrong PIN shows an
///   inline error and lets the user retry rather than closing - only Cancel backs out.</summary>
public partial class KioskPinWindow : Window, INotifyPropertyChanged
{
    private readonly string? _expectedHash;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public bool IsSetMode => _expectedHash is null;
    public bool IsVerifyMode => _expectedHash is not null;

    private string _prompt = "Choose a PIN. You'll need it again to turn Kiosk Mode off.";
    public string Prompt
    {
        get => _prompt;
        set { _prompt = value; OnPropertyChanged(); }
    }

    private string _errorText = "";
    public string ErrorText
    {
        get => _errorText;
        set { _errorText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => ErrorText.Length > 0;

    public string? ResultPinHash { get; private set; }

    /// <summary>Set mode - choosing a new PIN.</summary>
    public KioskPinWindow()
    {
        _expectedHash = null;
        InitializeComponent();
        DataContext = this;
        this.HideWindowIcon();
        Loaded += (_, _) => PinBox.Focus();
    }

    /// <summary>Verify mode - confirming the existing PIN.</summary>
    public KioskPinWindow(string expectedHash, string prompt = "Enter your Kiosk Mode PIN.")
    {
        _expectedHash = expectedHash;
        _prompt = prompt;
        InitializeComponent();
        DataContext = this;
        this.HideWindowIcon();
        Loaded += (_, _) => VerifyPinBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsSetMode)
        {
            var pin = PinBox.Password;
            if (pin.Length == 0)
            {
                ErrorText = "Enter a PIN.";
                return;
            }

            if (pin != ConfirmPinBox.Password)
            {
                ErrorText = "PINs don't match.";
                ConfirmPinBox.Password = "";
                return;
            }

            ResultPinHash = PinHasher.Hash(pin);
            DialogResult = true;
            Close();
        }
        else
        {
            if (!PinHasher.Verify(VerifyPinBox.Password, _expectedHash))
            {
                ErrorText = "Wrong PIN.";
                VerifyPinBox.Password = "";
                VerifyPinBox.Focus();
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
