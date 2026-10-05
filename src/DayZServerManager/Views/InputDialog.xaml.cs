using System.Windows;
using System.Windows.Input;

namespace DayZServerManager.Views;

public partial class InputDialog : Window
{
    /// <param name="code">true : code court en majuscules (Steam Guard) ; false : texte libre.</param>
    public InputDialog(string header, string message, bool code = true)
    {
        InitializeComponent();
        HeaderText.Text = header;
        MessageText.Text = message;
        if (!code)
        {
            ValueBox.CharacterCasing = System.Windows.Controls.CharacterCasing.Normal;
            ValueBox.MaxLength = 200;
            ValueBox.FontSize = 14;
            ValueBox.Height = 44;
            ValueBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            ValueBox.HorizontalContentAlignment = HorizontalAlignment.Left;
            ValueBox.TextAlignment = TextAlignment.Left;
        }
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
