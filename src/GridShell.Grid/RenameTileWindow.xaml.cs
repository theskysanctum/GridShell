using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GridShell.Core.Interop;

namespace GridShell;

public partial class RenameTileWindow : Window, INotifyPropertyChanged
{
    private string _newName;

    public string NewName
    {
        get => _newName;
        set
        {
            if (_newName != value)
            {
                _newName = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public RenameTileWindow(string currentName)
    {
        _newName = currentName;
        InitializeComponent();
        DataContext = this;
        this.ExcludeFromTaskbar();
        this.RecenterOnDisplayChange();
        this.HideWindowIcon();

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Rename_Click(object sender, RoutedEventArgs e) => Submit();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
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
        if (string.IsNullOrWhiteSpace(NewName))
        {
            return;
        }

        NewName = NewName.Trim();
        DialogResult = true;
        Close();
    }
}
