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

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            Log("Choisis d'abord un dossier d'installation.");
            return;
        }
        Log("Le téléchargement automatique arrive dans la prochaine version.");
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

        _dependenciesOk = deps.All(d => d.Installed);
        UpdateSteps();
    }

    private void UpdateSteps()
    {
        int current = string.IsNullOrWhiteSpace(FolderBox.Text) ? 0 : !_dependenciesOk ? 1 : 2;
        StepBadge.Text = $"Étape {current + 1} sur {StepNames.Length}";

        StepsGrid.Children.Clear();
        for (int i = 0; i < StepNames.Length; i++)
            StepsGrid.Children.Add(BuildStep(i, current));
    }

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
