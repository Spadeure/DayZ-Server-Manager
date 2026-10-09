using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

/// <summary>Choix du type d'un fichier d'économie (et de son nom quand on crée un fichier vide).</summary>
public partial class CeFileWindow : Window
{
    private readonly bool _askName;

    public CeFileWindow(string header, string subtitle, string selectedType, bool askName)
    {
        InitializeComponent();
        _askName = askName;
        HeaderText.Text = header;
        SubText.Text = subtitle;
        NamePanel.Visibility = askName ? Visibility.Visible : Visibility.Collapsed;

        foreach (var info in EconomyService.Types)
        {
            TypesPanel.Children.Add(new RadioButton
            {
                GroupName = "cetype",
                Tag = info.Type,
                Content = $"{info.Type}  —  {info.Label}",
                Style = (Style)Application.Current.FindResource("ChipButton"),
                Height = 38,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsChecked = info.Type == selectedType,
            });
        }

        Loaded += (_, _) =>
        {
            if (askName) NameBox.Focus();
        };
    }

    public string FileName => NameBox.Text.Trim();

    public string SelectedType =>
        TypesPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "types";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_askName && FileName.Length == 0)
        {
            ErrorText.Text = "Donne un nom au fichier.";
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
