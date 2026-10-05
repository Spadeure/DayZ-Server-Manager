using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        var settings = AppSettings.Current;
        AutoStartServerToggle.IsChecked = settings.AutoStartServer;
        CloseToTrayToggle.IsChecked = settings.CloseToTray;
        AutoUpdateServerToggle.IsChecked = settings.AutoUpdateServer;
        AutoUpdateModsToggle.IsChecked = settings.AutoUpdateMods;
        StableChip.IsChecked = settings.ServerBranch != "experimental";
        ExperimentalChip.IsChecked = settings.ServerBranch == "experimental";
        UpdateBranchInfo();
    }

    /// <summary>Relit l'état réel (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh()
    {
        try { StartWithWindowsToggle.IsChecked = StartupService.IsEnabled(); }
        catch { StartWithWindowsToggle.IsChecked = false; }
    }

    private void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (StartWithWindowsToggle.IsChecked == true)
            {
                StartupService.Enable();
                ShowStatus("Stryxhost Manager se lancera à l'ouverture de Windows, réduit près de l'horloge.", "CyanBrush");
            }
            else
            {
                StartupService.Disable();
                ShowStatus("Lancement avec Windows désactivé.", "MutedBrush");
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
            Refresh();
        }
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Current;
        settings.AutoStartServer = AutoStartServerToggle.IsChecked == true;
        settings.CloseToTray = CloseToTrayToggle.IsChecked == true;
        settings.AutoUpdateServer = AutoUpdateServerToggle.IsChecked == true;
        settings.AutoUpdateMods = AutoUpdateModsToggle.IsChecked == true;
        settings.Save();
        ShowStatus("Enregistré.", "CyanBrush");
    }

    private void Branch_Click(object sender, RoutedEventArgs e)
    {
        var branch = ExperimentalChip.IsChecked == true ? "experimental" : "stable";
        if (branch == AppSettings.Current.ServerBranch) return;
        AppSettings.Current.ServerBranch = branch;
        AppSettings.Current.Save();
        UpdateBranchInfo();
        ShowStatus("Version changée : va dans l'onglet Installation et clique sur « Mettre à jour le serveur » pour télécharger cette version.", "CyanBrush");
    }

    private void UpdateBranchInfo()
    {
        BranchInfo.Text = AppSettings.Current.ServerBranch == "experimental"
            ? "Expérimentale : la future version de DayZ, en test. Les joueurs doivent aussi utiliser DayZ Experimental, et beaucoup de mods ne sont pas compatibles."
            : "Stable : la version normale de DayZ, celle de tous les joueurs. Recommandée.";
    }

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)Application.Current.FindResource(brushKey);
    }
}
