using System.Windows;
using System.Windows.Input;
using GridShell.Core.Interop;

namespace GridShell;

/// <summary>A small modal text-entry dialog for Rename/New Folder - a plain WPF TextBox for
/// mouse/keyboard, plus Accept/Back/OpenKeyboard on its own ControllerInput so it's usable
/// without a physical keyboard too (Y summons the on-screen keyboard, which types into
/// whatever has focus - this dialog's TextBox, since the OSK itself never takes focus).</summary>
public partial class TextInputDialog : Window
{
    private readonly ControllerInput _controller = new(NavigationStick.None);
    private OnScreenKeyboardWindow? _osk;
    private bool _isClosing;

    public string? ResultText { get; private set; }

    public TextInputDialog(string title, string prompt, string initialText)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialText;

        _controller.ActionTriggered += OnControllerAction;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
            _controller.Start();
        };
        Closed += (_, _) =>
        {
            _controller.ActionTriggered -= OnControllerAction;
            _controller.Stop();
            _osk?.Close();
        };
    }

    private void OnControllerAction(ControllerAction action)
    {
        switch (action)
        {
            case ControllerAction.Accept:
                Confirm();
                break;
            case ControllerAction.Back:
                CloseDialog(false);
                break;
            case ControllerAction.OpenKeyboard:
                _osk ??= new OnScreenKeyboardWindow();
                _osk.OpenIfClosed();
                break;
        }
    }

    private void Confirm()
    {
        var text = InputBox.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        ResultText = text;
        CloseDialog(true);
    }

    private void CloseDialog(bool? result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        DialogResult = result;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Confirm();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => CloseDialog(false);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Confirm();
                break;
            case Key.Escape:
                CloseDialog(false);
                break;
        }
    }
}
