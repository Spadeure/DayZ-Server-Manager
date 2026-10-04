using System.Windows;
using System.Windows.Input;

namespace DayZServerManager.Views;

public partial class SteamLoginWindow : Window
{
    public SteamLoginWindow(string? savedUser)
    {
        InitializeComponent();
        UserBox.Text = savedUser ?? "";
        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(UserBox.Text)) UserBox.Focus();
            else PassBox.Focus();
        };
    }

    public string UserName => UserBox.Text.Trim();
    public string Password => PassBox.Password;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (UserName.Length == 0 || Password.Length == 0)
        {
            ErrorText.Text = "Remplis le nom d'utilisateur et le mot de passe.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
