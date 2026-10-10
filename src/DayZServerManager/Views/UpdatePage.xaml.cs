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

    /// <summary>Numéro de la nouvelle version disponible (null s'il n'y en a pas).</summary>
    public event Action<string?>? LatestVersionChanged;

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
            var releases = await UpdateService.GetReleasesAsync();
            _latest = releases.FirstOrDefault(r => r.DownloadUrl.Length > 0);
            BuildPatchNotes(releases);

            if (_latest == null)
            {
                LatestText.Text = "—";
                StatusText.Text = "Aucune version publiée pour le moment.";
                UpdateAvailabilityChanged?.Invoke(false);
                LatestVersionChanged?.Invoke(null);
                return;
            }

            bool available = _latest.Version > UpdateService.CurrentVersion;
            LatestText.Text = _latest.Version.ToString(3);
            StatusText.Text = available ? "Une nouvelle version est disponible !" : "Tu as la dernière version.";
            StatusText.Foreground = Res(available ? "AccentBrush" : "CyanBrush");
            UpdateButton.IsEnabled = available;
            UpdateAvailabilityChanged?.Invoke(available);
            LatestVersionChanged?.Invoke(available ? _latest.Version.ToString(3) : null);
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

    // ===== Patch notes =====

    private void BuildPatchNotes(List<ReleaseInfo> releases)
    {
        NotesPanel.Children.Clear();

        // Les petites corrections republient les mêmes notes : on ne garde qu'une entrée par patch note.
        var entries = new List<ReleaseInfo>();
        string? previousNotes = null;
        foreach (var release in releases.OrderBy(r => r.Version))
        {
            var notes = Normalize(release.Notes);
            if (notes.Length == 0 || notes == previousNotes) continue;
            previousNotes = notes;
            entries.Add(release);
        }
        entries.Reverse();

        if (entries.Count == 0)
        {
            NotesPanel.Children.Add(new TextBlock
            {
                Text = "Aucun patch note pour le moment.",
                FontSize = 13,
                Foreground = Res("MutedBrush"),
            });
            return;
        }

        var current = UpdateService.CurrentVersion;
        foreach (var release in entries.Take(15))
            NotesPanel.Children.Add(BuildEntry(release, current, NotesPanel.Children.Count == 0));
    }

    private UIElement BuildEntry(ReleaseInfo release, Version current, bool first)
    {
        var panel = new StackPanel { Margin = new Thickness(0, first ? 0 : 22, 0, 0) };

        // En-tête : version, badge, date.
        var header = new DockPanel { LastChildFill = false };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = $"VERSION {release.Version.ToString(3)}",
            FontFamily = (FontFamily)Application.Current.FindResource("Orbitron"),
            FontSize = 14,
            Foreground = Res(release.Version > current ? "AccentBrush" : "TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        string? badge = release.Version > current ? "NOUVEAU" : release.Version == current ? "INSTALLÉE" : null;
        if (badge != null)
        {
            bool isNew = badge == "NOUVEAU";
            title.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = isNew ? Res("AccentBrush") : Res("CyanSoftBrush"),
                Child = new TextBlock
                {
                    Text = badge,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Res(isNew ? "AccentTextBrush" : "CyanBrush"),
                },
            });
        }
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);

        var date = new TextBlock
        {
            Text = release.Published.ToString("dd/MM/yyyy"),
            FontSize = 12,
            Foreground = Res("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(date, Dock.Right);
        header.Children.Add(date);

        // Retour en arrière : réinstaller une ancienne version en un clic.
        if (release.Version < current && release.DownloadUrl.Length > 0)
        {
            var rollback = new Button
            {
                Content = "Revenir à cette version",
                Style = (Style)Application.Current.FindResource("SmallButton"),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            rollback.Click += async (_, _) => await RollbackAsync(release);
            DockPanel.SetDock(rollback, Dock.Right);
            header.Children.Add(rollback);
        }
        panel.Children.Add(header);

        // Lignes du patch note : « • texte » devient une puce, le reste un sous-titre.
        foreach (var rawLine in release.Notes.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            bool isBullet = line.StartsWith('•') || line.StartsWith('-') || line.StartsWith('*');
            if (!isBullet)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Res("MutedBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 0),
                });
                continue;
            }

            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = "•", FontSize = 14, Foreground = Res("CyanBrush") });
            var text = new TextBlock
            {
                Text = line.TrimStart('•', '-', '*', ' '),
                FontSize = 13,
                LineHeight = 20,
                Foreground = Res("ConsoleTextBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static string Normalize(string notes) => notes.Replace("\r", "").Trim();

    // ===== Actions =====

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_latest == null) return;
        await InstallAsync(_latest);
    }

    private async Task RollbackAsync(ReleaseInfo release)
    {
        if (_busy) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"Revenir à la version {release.Version.ToString(3)} ?\n\n" +
            "L'application va télécharger cette version puis redémarrer. " +
            "Tes réglages et ton serveur ne sont pas touchés.\n\n" +
            "Tu pourras toujours remettre la dernière version plus tard avec « Mettre à jour ».",
            "Stryxhost Manager", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        await InstallAsync(release);
    }

    private async Task InstallAsync(ReleaseInfo release)
    {
        if (_busy) return;
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
            await UpdateService.DownloadAndInstallAsync(release, progress, CancellationToken.None);
            DownloadText.Text = "Redémarrage de l'application…";
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            DownloadText.Text = $"Échec de la mise à jour : {ex.Message}";
            _busy = false;
            CheckButton.IsEnabled = true;
            UpdateButton.IsEnabled = _latest != null && _latest.Version > UpdateService.CurrentVersion;
        }
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
