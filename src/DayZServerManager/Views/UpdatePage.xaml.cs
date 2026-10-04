using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class UpdatePage : UserControl
{
    private ReleaseInfo? _latest;
    private bool _busy;

    /// <summary>Prévient la fenêtre principale quand une mise à jour est (ou n'est plus) disponible.</summary>
    public event Action<bool>? UpdateAvailabilityChanged;

    public UpdatePage()
    {
        InitializeComponent();
        var current = UpdateService.CurrentVersion;
        CurrentText.Text = current.ToString(3);
        CurrentKind.Text = current.Major == 0 ? "Version de test" : "Version officielle";
    }

    public async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true;
        CheckButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        StatusText.Text = "Vérification en cours…";
        StatusText.Foreground = Res("MutedBrush");

        try
        {
            _latest = await UpdateService.GetLatestAsync();
            if (_latest == null)
            {
                LatestText.Text = "—";
                StatusText.Text = "Aucune version publiée pour le moment.";
                NotesText.Text = "";
                DateText.Text = "";
                UpdateAvailabilityChanged?.Invoke(false);
                return;
            }

            bool available = _latest.Version > UpdateService.CurrentVersion;
            LatestText.Text = _latest.Version.ToString(3);
            DateText.Text = $"Publiée le {_latest.Published:dd/MM/yyyy à HH:mm}";
            NotesText.Text = string.IsNullOrWhiteSpace(_latest.Notes) ? "Pas de notes pour cette version." : _latest.Notes.Trim();
            StatusText.Text = available ? "Une nouvelle version est disponible !" : "Tu as la dernière version.";
            StatusText.Foreground = Res(available ? "AccentBrush" : "CyanBrush");
            UpdateButton.IsEnabled = available;
            UpdateAvailabilityChanged?.Invoke(available);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Impossible de vérifier : {ex.Message}";
            StatusText.Foreground = Res("WarnBrush");
        }
        finally
        {
            _busy = false;
            CheckButton.IsEnabled = true;
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_latest == null || _busy) return;
        _busy = true;
        CheckButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        DownloadPanel.Visibility = Visibility.Visible;
        DownloadText.Text = "Téléchargement : 0 %";

        var progress = new Progress<double>(p =>
        {
            DownloadBar.Value = p;
            DownloadText.Text = $"Téléchargement : {p:0} %";
        });

        try
        {
            await UpdateService.DownloadAndInstallAsync(_latest, progress, CancellationToken.None);
            DownloadText.Text = "Redémarrage de l'application…";
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            DownloadText.Text = $"Échec de la mise à jour : {ex.Message}";
            _busy = false;
            CheckButton.IsEnabled = true;
            UpdateButton.IsEnabled = true;
        }
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
