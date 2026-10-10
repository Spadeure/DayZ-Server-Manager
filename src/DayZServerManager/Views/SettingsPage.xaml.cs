using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class SettingsPage : UserControl
{
    /// <summary>Demande à la fenêtre principale d'ouvrir l'assistant d'installation.</summary>
    public event Action? InstallRequested;

    private bool _ready;

    public SettingsPage()
    {
        InitializeComponent();
        var settings = AppSettings.Current;
        AutoRestartToggle.IsChecked = settings.AutoRestart;
        ScheduledToggle.IsChecked = settings.ScheduledRestart;
        WarnToggle.IsChecked = settings.WarnBeforeRestart;
        HoursBox.Text = settings.RestartHours.ToString();
        AutoStartServerToggle.IsChecked = settings.AutoStartServer;
        CloseToTrayToggle.IsChecked = settings.CloseToTray;
        AutoUpdateServerToggle.IsChecked = settings.AutoUpdateServer;
        AutoUpdateModsToggle.IsChecked = settings.AutoUpdateMods;
        StableChip.IsChecked = settings.ServerBranch != "experimental";
        ExperimentalChip.IsChecked = settings.ServerBranch == "experimental";
        UpdateBranchInfo();
        _ready = true;
    }

    /// <summary>Relit l'état réel (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh()
    {
        var folder = AppSettings.Current.ServerFolder;
        InstallInfo.Text = DependencyChecker.IsServerInstalled(folder)
            ? $"Serveur installé dans {folder}. Change de dossier, réinstalle les dépendances ou mets à jour le serveur."
            : "Le serveur n'est pas encore installé : l'assistant te guide en quatre étapes.";

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

    private void Install_Click(object sender, RoutedEventArgs e) => InstallRequested?.Invoke();

    private void LaunchSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new LaunchWindow { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true) return;
        ShowStatus(ServerManager.Instance.IsRunning
            ? "Paramètres de lancement enregistrés : ils seront appliqués au prochain redémarrage."
            : "Paramètres de lancement enregistrés.", "CyanBrush");
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var settings = AppSettings.Current;
        settings.AutoRestart = AutoRestartToggle.IsChecked == true;
        settings.ScheduledRestart = ScheduledToggle.IsChecked == true;
        settings.WarnBeforeRestart = WarnToggle.IsChecked == true;
        if (int.TryParse(HoursBox.Text.Trim(), out var hours) && hours >= 1 && hours <= 48)
            settings.RestartHours = hours;
        else
            HoursBox.Text = settings.RestartHours.ToString();
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
        ShowStatus("Version changée : clique sur « Ouvrir l'installation » ci-dessus, puis « Mettre à jour le serveur », pour télécharger cette version.", "CyanBrush");
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
