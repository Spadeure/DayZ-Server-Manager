using System.Windows;
using System.Windows.Input;

namespace DayZServerManager.Views;

public partial class InputDialog : Window
{
    public InputDialog(string header, string message)
    {
        InitializeComponent();
        HeaderText.Text = header;
        MessageText.Text = message;
        Loaded += (_, _) => ValueBox.Focus();
    }

    public string Value => ValueBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0) return;
        DialogResult = true;
    }

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
