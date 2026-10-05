using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class LaunchWindow : Window
{
    private readonly bool _ready;

    public LaunchWindow()
    {
        InitializeComponent();
        var settings = AppSettings.Current;
        ExtraBox.Text = settings.ExtraLaunchArgs;
        CustomToggle.IsChecked = settings.UseCustomLaunchLine;
        CustomBox.Text = string.IsNullOrWhiteSpace(settings.CustomLaunchLine) ? AutomaticLine() : settings.CustomLaunchLine;
        _ready = true;
        UpdatePreview();
    }

    private string AutomaticLine()
    {
        var extra = ExtraBox.Text.Trim();
        var line = ServerManager.BuildDefaultArguments();
        return extra.Length == 0 ? line : $"{line} {extra}";
    }

    private void UpdatePreview()
    {
        if (!_ready) return;
        bool custom = CustomToggle.IsChecked == true;
        CustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        ExtraPanel.IsEnabled = !custom;
        ExtraPanel.Opacity = custom ? 0.4 : 1;
        PreviewText.Text = "DayZServer_x64.exe " + (custom ? CustomBox.Text.Trim() : AutomaticLine());
    }

    private void Text_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void Custom_Click(object sender, RoutedEventArgs e) => UpdatePreview();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        CustomBox.Text = AutomaticLine();
        UpdatePreview();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PreviewText.Text);
            ShowStatus("Ligne copiée.", "CyanBrush");
        }
        catch
        {
            ShowStatus("Impossible de copier pour le moment, réessaie.", "WarnBrush");
        }
    }

    private void Detect_Click(object sender, RoutedEventArgs e)
    {
        if (!DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder))
        {
            ShowStatus("Installe d'abord le serveur.", "WarnBrush");
            return;
        }
        try
        {
            var found = ModService.DetectManualMods(ServerManager.ServerFolder, AppSettings.Current.Mods);
            AppSettings.Current.Mods.AddRange(found);
            AppSettings.Current.Save();
            UpdatePreview();
            ShowStatus(found.Count == 0
                ? "Aucun nouveau mod trouvé dans le dossier du serveur."
                : $"{found.Count} mod(s) ajouté(s) : {string.Join(", ", found.Select(m => m.Name))}. Tu peux changer leur ordre dans l'onglet Mods.",
                found.Count == 0 ? "MutedBrush" : "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        bool custom = CustomToggle.IsChecked == true;
        if (custom && CustomBox.Text.Trim().Length == 0)
        {
            ShowStatus("La ligne personnalisée est vide.", "WarnBrush");
            return;
        }

        var settings = AppSettings.Current;
        settings.ExtraLaunchArgs = ExtraBox.Text.Trim();
        settings.UseCustomLaunchLine = custom;
        settings.CustomLaunchLine = CustomBox.Text.Trim();
        settings.Save();
        DialogResult = true;
    }

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)Application.Current.FindResource(brushKey);
    }

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
