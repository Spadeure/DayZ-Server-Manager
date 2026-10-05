using System.Windows;
using System.Windows.Input;

namespace DayZServerManager.Views;

public partial class BanWindow : Window
{
    public BanWindow(string playerName)
    {
        InitializeComponent();
        HeaderText.Text = $"BANNIR {playerName.ToUpperInvariant()}";
        Loaded += (_, _) => ReasonBox.Focus();
    }

    /// <summary>Durée en minutes (0 = définitif).</summary>
    public int Minutes =>
        HourChip.IsChecked == true ? 60
        : DayChip.IsChecked == true ? 1440
        : WeekChip.IsChecked == true ? 10080
        : 0;

    public string Reason => ReasonBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
