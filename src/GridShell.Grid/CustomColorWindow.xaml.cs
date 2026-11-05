using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GridShell.Core.Interop;

namespace GridShell;

public partial class CustomColorWindow : Window, INotifyPropertyChanged
{
    private static readonly Regex HexPattern = new("^#?[0-9A-Fa-f]{6}$");

    private string _hexText;
    private bool _showError;

    public string HexText
    {
        get => _hexText;
        set
        {
            if (_hexText != value)
            {
                _hexText = value;
                OnPropertyChanged();
                ShowError = false;
                OnPropertyChanged(nameof(PreviewBrush));
            }
        }
    }

    public bool ShowError
    {
        get => _showError;
        set
        {
            if (_showError != value)
            {
                _showError = value;
                OnPropertyChanged();
            }
        }
    }

    public Brush PreviewBrush =>
        TryNormalize(HexText, out var normalized)
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(normalized)!)
            : Brushes.Transparent;

    public string? Hex { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public CustomColorWindow(string currentHex)
    {
        _hexText = currentHex;
        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();

        Loaded += (_, _) =>
        {
            HexBox.Focus();
            HexBox.SelectAll();
        };
    }

    private static bool TryNormalize(string input, out string normalized)
    {
        var trimmed = input.Trim();
        if (!HexPattern.IsMatch(trimmed))
        {
            normalized = "";
            return false;
        }

        normalized = trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
        return true;
    }

    private void UseColor_Click(object sender, RoutedEventArgs e) => Submit();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Submit()
    {
        if (!TryNormalize(HexText, out var normalized))
        {
            ShowError = true;
            return;
        }

        Hex = normalized.ToUpperInvariant();
        DialogResult = true;
        Close();
    }
}
