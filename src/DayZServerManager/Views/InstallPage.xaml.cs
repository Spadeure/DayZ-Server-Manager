using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;
using Microsoft.Win32;

namespace DayZServerManager.Views;

public partial class InstallPage : UserControl
{
    private static readonly string[] StepNames = ["Dossier", "Dépendances", "Fichiers serveur", "Pare-feu"];

    private bool _dependenciesOk;
    private CancellationTokenSource? _cts;

    public InstallPage()
    {
        InitializeComponent();

        FolderBox.Text = AppSettings.Current.ServerFolder;
        Log("Bienvenue dans DayZ Server Manager.");
        Log(string.IsNullOrWhiteSpace(FolderBox.Text)
            ? "Choisis un dossier d'installation pour commencer."
            : $"Dossier d'installation : {FolderBox.Text}");

        UpdateFreeSpace();
        RefreshDependencies();
    }

    // ===== Actions =====

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choisis le dossier d'installation du serveur" };
        if (Directory.Exists(FolderBox.Text)) dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog() != true) return;

        FolderBox.Text = dialog.FolderName;
        AppSettings.Current.ServerFolder = dialog.FolderName;
        AppSettings.Current.Save();
        Log($"Dossier choisi : {dialog.FolderName}");

        UpdateFreeSpace();
        RefreshDependencies();
    }

    private void Recheck_Click(object sender, RoutedEventArgs e)
    {
        RefreshDependencies();
        Log("Dépendances revérifiées.");
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        // Pendant une installation, le bouton sert à annuler.
        if (_cts != null)
        {
            _cts.Cancel();
            Log("Annulation en cours…");
            return;
        }

        var folder = FolderBox.Text;
        if (string.IsNullOrWhiteSpace(folder))
        {
            Log("Choisis d'abord un dossier d'installation.");
            return;
        }

        var login = new SteamLoginWindow(AppSettings.Current.SteamUser) { Owner = Window.GetWindow(this) };
        if (login.ShowDialog() != true)
        {
            Log("Installation annulée.");
            return;
        }
        AppSettings.Current.SteamUser = login.UserName;
        AppSettings.Current.Save();

        var steam = new SteamCmdService(folder);
        steam.Output += line => Dispatcher.Invoke(() => Log(line));
        steam.Progress += (stage, percent, done, total) =>
            Dispatcher.Invoke(() => ShowProgress(stage, percent, done, total));
        steam.AskSteamGuardCode = () => Dispatcher.Invoke(() =>
        {
            var dialog = new InputDialog("CODE STEAM GUARD",
                "Ouvre l'application Steam sur ton téléphone, va dans Steam Guard et entre le code affiché.")
            {
                Owner = Window.GetWindow(this),
            };
            return dialog.ShowDialog() == true ? dialog.Value : null;
        });

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            if (!File.Exists(steam.SteamCmdExe))
            {
                ShowProgress("Installation de SteamCMD", 0, 0, 0);
                await steam.InstallSteamCmdAsync(_cts.Token);
                RefreshDependencies();
            }

            ShowProgress("Connexion à Steam", 0, 0, 0);
            Log("Connexion à Steam et téléchargement des fichiers serveur…");
            var result = await steam.DownloadServerAsync(login.UserName, login.Password, _cts.Token);

            if (result.ServerInstallSucceeded && DependencyChecker.IsServerInstalled(folder))
            {
                ShowProgress("Installation terminée", 100, 0, 0);
                Log($"Le serveur DayZ est installé dans : {steam.ServerFolder}");
            }
            else
            {
                ProgressInfo.Text = "Échec";
                Log($"Le téléchargement n'a pas abouti (code {result.ExitCode}). Lis les messages ci-dessus.");
            }
        }
        catch (OperationCanceledException)
        {
            ProgressInfo.Text = "Annulé";
            Log("Installation annulée.");
        }
        catch (Exception ex)
        {
            ProgressInfo.Text = "Échec";
            Log($"Erreur : {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
            RefreshDependencies();
        }
    }

    // ===== Affichage =====

    private void UpdateFreeSpace()
    {
        if (string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            SpaceText.Text = "Aucun dossier choisi.";
            return;
        }
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(FolderBox.Text)!);
            SpaceText.Text = $"Espace libre sur {drive.Name} : {drive.AvailableFreeSpace / 1_073_741_824.0:0.#} Go";
        }
        catch
        {
            SpaceText.Text = "Espace libre : inconnu";
        }
    }

    private void RefreshDependencies()
    {
        DepsPanel.Children.Clear();
        var deps = DependencyChecker.Check(FolderBox.Text);

        foreach (var dep in deps)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = dep.Name, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = Res("TextBrush"),
            });
            texts.Children.Add(new TextBlock
            {
                Text = dep.Description, FontSize = 12, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 2, 0, 0),
            });
            row.Children.Add(texts);

            var pill = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                Background = Res(dep.Installed ? "CyanSoftBrush" : "WarnSoftBrush"),
                Child = new TextBlock
                {
                    Text = dep.Installed ? "Installé" : "Manquant",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Res(dep.Installed ? "CyanBrush" : "WarnBrush"),
                },
            };
            Grid.SetColumn(pill, 1);
            row.Children.Add(pill);

            DepsPanel.Children.Add(new Border
            {
                BorderBrush = Res("LineBrush"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 12, 0, 12),
                Child = row,
            });
        }

        _dependenciesOk = deps.Where(d => d.Name != "SteamCMD").All(d => d.Installed);
        UpdateSteps();
    }

    private void UpdateSteps()
    {
        bool hasFolder = !string.IsNullOrWhiteSpace(FolderBox.Text);
        bool serverInstalled = DependencyChecker.IsServerInstalled(FolderBox.Text);
        int current = !hasFolder ? 0 : serverInstalled ? 3 : _dependenciesOk ? 2 : 1;
        StepBadge.Text = $"Étape {current + 1} sur {StepNames.Length}";

        if (_cts == null)
            StartButton.Content = serverInstalled ? "Mettre à jour le serveur" : "Lancer l'installation";

        StepsGrid.Children.Clear();
        for (int i = 0; i < StepNames.Length; i++)
            StepsGrid.Children.Add(BuildStep(i, current));
    }

    private void SetBusy(bool busy)
    {
        BrowseButton.IsEnabled = !busy;
        RecheckButton.IsEnabled = !busy;
        if (busy) StartButton.Content = "Annuler";
        else UpdateSteps();
    }

    private void ShowProgress(string stage, double percent, long done, long total)
    {
        Progress.Value = Math.Clamp(percent, 0, 100);
        PercentText.Text = $"{percent:0} %";
        ProgressInfo.Text = total > 0 ? $"{stage} · {FormatSize(done)} sur {FormatSize(total)}" : stage;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.0} Go" : $"{bytes / 1_048_576.0:0} Mo";

    private UIElement BuildStep(int index, int current)
    {
        bool done = index < current;
        bool active = index == current;

        var circle = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Res(done || active ? "AccentBrush" : "Panel2Brush"),
            Child = new TextBlock
            {
                Text = done ? "✓" : (index + 1).ToString(),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Res(done || active ? "AccentTextBrush" : "MutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = StepNames[index],
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res(index <= current ? "TextBrush" : "MutedBrush"),
        });
        texts.Children.Add(new TextBlock
        {
            Text = done ? "Terminé" : active ? "En cours" : "En attente",
            FontSize = 12,
            Foreground = Res("MutedBrush"),
        });

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(circle);
        content.Children.Add(texts);

        return new Border
        {
            Background = Res(active ? "Panel2Brush" : "PanelBrush"),
            BorderBrush = Res(active ? "AccentBrush" : "LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(index == 0 ? 0 : 5, 0, index == StepNames.Length - 1 ? 0 : 5, 0),
            Child = content,
        };
    }

    private void Log(string message)
    {
        LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        LogScroll.ScrollToEnd();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
