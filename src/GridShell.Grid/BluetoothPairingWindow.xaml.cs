using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using GridShell.Core.Interop;
using GridShell.Core.Interop.Bluetooth;
using Windows.Devices.Enumeration;

namespace GridShell;

public partial class BluetoothPairingWindow : Window, INotifyPropertyChanged
{
    private readonly PairingRequest _request;
    private string _enteredPin = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string DeviceName { get; }
    public string DisplayPin => _request.DisplayPin;
    public bool ShowsPin => IsDisplayPin || IsConfirmPinMatch;
    public bool IsConfirmOnly => _request.Kind == DevicePairingKinds.ConfirmOnly;
    public bool IsDisplayPin => _request.Kind == DevicePairingKinds.DisplayPin;
    public bool IsConfirmPinMatch => _request.Kind == DevicePairingKinds.ConfirmPinMatch;
    public bool IsProvidePin => _request.Kind == DevicePairingKinds.ProvidePin;

    public string EnteredPin
    {
        get => _enteredPin;
        set { _enteredPin = value; OnPropertyChanged(); }
    }

    public BluetoothPairingWindow(string deviceName, PairingRequest request)
    {
        DeviceName = deviceName;
        _request = request;

        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();
        this.HideWindowIcon();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        _request.Respond(true, IsProvidePin ? EnteredPin : null);
        Close();
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        _request.Respond(false, null);
        Close();
    }
}
