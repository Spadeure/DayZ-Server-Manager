using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class BackupsPage : UserControl
{
    private bool _busy;

    public BackupsPage()
    {
        InitializeComponent();
        AutoToggle.IsChecked = AppSettings.Current.AutoBackup;
        KeepBox.Text = AppSettings.Current.BackupKeep.ToString();
    }

    /// <summary>Recharge la liste (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh() => BuildList();

    // ===== Actions =====

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !CheckInstalled()) return;
        if (ServerManager.Instance.IsRunning &&
            !Confirm("Le serveur est en ligne : la sauvegarde peut être incomplète si des joueurs jouent en ce moment.\n\nSauvegarder quand même ?"))
            return;

        await RunAsync("Sauvegarde en cours…", () =>
        {
            var backup = BackupService.Create("manuel");
            return backup == null
                ? "Rien à sauvegarder pour l'instant : la persistance est créée au premier démarrage du serveur."
                : $"Sauvegarde créée : {backup.FileName}";
        });
    }

    private async void Restore(BackupInfo backup)
    {
        if (_busy || !CheckInstalled() || !CheckStopped()) return;
        if (!Confirm($"Restaurer la sauvegarde du {backup.Date:dd/MM/yyyy à HH:mm} ?\n\n" +
                     "La progression actuelle sera remplacée (une sauvegarde de sécurité est faite avant).")) return;

        await RunAsync("Restauration en cours…", () =>
        {
            BackupService.Restore(backup);
            return $"Sauvegarde du {backup.Date:dd/MM/yyyy à HH:mm} restaurée. Tu peux redémarrer le serveur.";
        });
    }

    private async void Wipe_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !CheckInstalled() || !CheckStopped()) return;
        if (!Confirm("Remettre la carte à zéro ?\n\nToutes les bases, objets, véhicules et personnages seront effacés.\n" +
                     "Une sauvegarde de sécurité est faite juste avant.")) return;
        if (!Confirm("Dernière confirmation : es-tu sûr de vouloir faire un wipe ?")) return;

        await RunAsync("Wipe en cours…", () =>
        {
            BackupService.Wipe();
            return "Wipe terminé ! La carte repartira à zéro au prochain démarrage du serveur.";
        });
    }

    private async void Delete(BackupInfo backup)
    {
        if (_busy) return;
        if (!Confirm($"Supprimer définitivement la sauvegarde du {backup.Date:dd/MM/yyyy à HH:mm} ?")) return;

        await RunAsync("Suppression…", () =>
        {
            BackupService.Delete(backup);
            return "Sauvegarde supprimée.";
        });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AppSettings.Current.ServerFolder)) return;
        Directory.CreateDirectory(BackupService.BackupFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupService.BackupFolder}\"") { UseShellExecute = true });
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Current;
        settings.AutoBackup = AutoToggle.IsChecked == true;
        if (int.TryParse(KeepBox.Text.Trim(), out var keep) && keep >= 1 && keep <= 500)
            settings.BackupKeep = keep;
        else
            KeepBox.Text = settings.BackupKeep.ToString();
        settings.Save();
    }

    private async Task RunAsync(string working, Func<string> action)
    {
        _busy = true;
        SetButtons(false);
        ShowStatus(working, "MutedBrush");
        try
        {
            var message = await Task.Run(action);
            ShowStatus(message, "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
        }
        finally
        {
            _busy = false;
            SetButtons(true);
            BuildList();
        }
    }

    // ===== Liste =====

    private void BuildList()
    {
        BackupsList.Children.Clear();
        List<BackupInfo> backups;
        try
        {
            backups = string.IsNullOrWhiteSpace(AppSettings.Current.ServerFolder)
                ? new List<BackupInfo>()
                : BackupService.List();
        }
        catch
        {
            backups = new List<BackupInfo>();
        }

        ListTitle.Text = $"SAUVEGARDES ({backups.Count})";
        if (backups.Count == 0)
        {
            BackupsList.Children.Add(new TextBlock
            {
                Text = "Aucune sauvegarde pour l'instant.",
                FontSize = 13,
                Foreground = Res("MutedBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        for (int i = 0; i < backups.Count; i++)
            BackupsList.Children.Add(BuildRow(backups[i], i));
    }

    private UIElement BuildRow(BackupInfo backup, int index)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = $"{backup.Date:dd/MM/yyyy} à {backup.Date:HH:mm:ss}",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = Res("TextBrush"),
        });
        texts.Children.Add(new TextBlock
        {
            Text = $"{backup.Map} · {backup.ReasonLabel} · {FormatSize(backup.Size)}",
            FontSize = 12,
            Foreground = Res("MutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        grid.Children.Add(texts);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var restore = new Button
        {
            Content = "Restaurer",
            Style = (Style)Application.Current.FindResource("SmallButton"),
            IsEnabled = !_busy,
        };
        restore.Click += (_, _) => Restore(backup);
        buttons.Children.Add(restore);

        var delete = new Button
        {
            Content = "Supprimer",
            Style = (Style)Application.Current.FindResource("SmallButton"),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = !_busy,
        };
        delete.Click += (_, _) => Delete(backup);
        buttons.Children.Add(delete);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return new Border
        {
            BorderBrush = Res("LineBrush"),
            BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 10),
            Child = grid,
        };
    }

    // ===== Outils =====

    private bool CheckInstalled()
    {
        if (DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder)) return true;
        ShowStatus("Installe d'abord le serveur depuis l'onglet Installation.", "WarnBrush");
        return false;
    }

    private bool CheckStopped()
    {
        if (!ServerManager.Instance.IsRunning) return true;
        ShowStatus("Arrête d'abord le serveur (onglet Serveur) : on ne peut pas modifier la persistance pendant qu'il tourne.", "WarnBrush");
        return false;
    }

    private bool Confirm(string message) =>
        MessageBox.Show(Window.GetWindow(this)!, message, "Stryxhost Manager",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void SetButtons(bool enabled)
    {
        BackupButton.IsEnabled = enabled;
        WipeButton.IsEnabled = enabled;
    }

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = Res(brushKey);
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.0} Go"
        : bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:0.0} Mo"
        : $"{Math.Max(1, bytes / 1024)} Ko";

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
