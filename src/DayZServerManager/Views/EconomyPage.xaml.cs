using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DayZServerManager.Services;
using Microsoft.Win32;

namespace DayZServerManager.Views;

public partial class EconomyPage : UserControl
{
    private record FolderView(CeFolder Folder, List<(CeFile File, CeCheck Check)> Files, List<string> Undeclared);

    private string? _mission;
    private bool _busy;

    /// <summary>Demande à la fenêtre principale d'ouvrir un fichier dans l'éditeur XML.</summary>
    public event Action<string>? EditRequested;

    public EconomyPage()
    {
        InitializeComponent();
    }

    /// <summary>Relit cfgeconomycore.xml et vérifie les fichiers (appelé à chaque ouverture de l'onglet).</summary>
    public async void Refresh()
    {
        if (_busy) return;
        _busy = true;
        FoldersPanel.Children.Clear();
        ShowStatus("Lecture des dossiers d'économie…", "MutedBrush");

        try
        {
            _mission = EconomyService.MissionFolder();
            if (_mission == null)
            {
                MissionText.Text = "Mission introuvable";
                CreateButton.IsEnabled = false;
                ShowEmpty("Installe d'abord le serveur (onglet Installation) : la mission et son fichier cfgeconomycore.xml seront alors disponibles.");
                ShowStatus("", "MutedBrush");
                return;
            }

            MissionText.Text = _mission;
            CreateButton.IsEnabled = true;
            var mission = _mission;

            // Lecture et vérification en arrière-plan : les fichiers types peuvent être gros.
            var (views, undeclaredFolders) = await Task.Run(() =>
            {
                var folders = EconomyService.ReadFolders(mission);
                var list = folders.Select(f => new FolderView(
                    f,
                    f.Files.Select(file => (file, EconomyService.Check(file))).ToList(),
                    EconomyService.FindUndeclaredFiles(f))).ToList();
                return (list, EconomyService.FindUndeclaredFolders(mission, folders));
            });

            Build(views, undeclaredFolders);
            int errors = views.Sum(v => v.Files.Count(f => !f.Check.Ok));
            ShowStatus(errors > 0
                ? $"{errors} fichier(s) à corriger (en orange). Clique sur « Modifier » pour les ouvrir dans l'éditeur."
                : "Les changements sont pris en compte au prochain démarrage du serveur.",
                errors > 0 ? "WarnBrush" : "MutedBrush");
        }
        catch (Exception ex)
        {
            ShowEmpty("Impossible de lire les dossiers d'économie.");
            ShowStatus(ex.Message, "WarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    // ===== Affichage =====

    private void Build(List<FolderView> views, List<string> undeclaredFolders)
    {
        FoldersPanel.Children.Clear();
        ListTitle.Text = $"DOSSIERS DÉCLARÉS ({views.Count})";

        foreach (var name in undeclaredFolders)
        {
            var row = Row($"📁 {name}", "Dossier trouvé dans la mission, mais pas déclaré : le serveur l'ignore.", "WarnBrush", 0);
            Buttons(row).Children.Add(ActionButton("Déclarer", () => DeclareFolder(name)));
            FoldersPanel.Children.Add(row);
        }

        if (views.Count == 0 && undeclaredFolders.Count == 0)
            ShowEmpty("Aucun dossier d'économie pour l'instant. Crée-en un ci-dessus pour ajouter les fichiers d'un mod.");

        foreach (var view in views) FoldersPanel.Children.Add(BuildFolder(view));
    }

    private UIElement BuildFolder(FolderView view)
    {
        var folder = view.Folder;
        var panel = new StackPanel();

        var header = Row(folder.Name.ToUpperInvariant(),
            folder.Exists ? $"{folder.Files.Count} fichier(s) déclaré(s)" : "Dossier introuvable sur le disque",
            folder.Exists ? "MutedBrush" : "WarnBrush", 0, header: true);
        var buttons = Buttons(header);
        buttons.Children.Add(ActionButton("Ajouter un fichier", () => AddFile(folder)));
        buttons.Children.Add(ActionButton("Créer un fichier vide", () => CreateEmptyFile(folder)));
        buttons.Children.Add(ActionButton("Ouvrir le dossier", () => OpenFolder(folder.FullPath)));
        buttons.Children.Add(ActionButton("Retirer", () => RemoveFolder(folder), warn: true));
        panel.Children.Add(header);

        for (int i = 0; i < view.Files.Count; i++)
        {
            var (file, check) = view.Files[i];
            var row = Row($"{file.Name}   ·   {file.Type}", check.Message, check.Ok ? "CyanBrush" : "WarnBrush", 1, indent: true);
            var rowButtons = Buttons(row);
            if (file.Exists) rowButtons.Children.Add(ActionButton("Modifier", () => EditRequested?.Invoke(file.FullPath)));
            rowButtons.Children.Add(ActionButton("Type", () => ChangeType(folder, file)));
            rowButtons.Children.Add(ActionButton("Retirer", () => RemoveFile(folder, file), warn: true));
            panel.Children.Add(row);
        }

        foreach (var name in view.Undeclared)
        {
            var row = Row(name, "Présent dans le dossier mais pas déclaré : le serveur l'ignore.", "WarnBrush", 1, indent: true);
            var rowButtons = Buttons(row);
            rowButtons.Children.Add(ActionButton("Modifier", () => EditRequested?.Invoke(Path.Combine(folder.FullPath, name))));
            rowButtons.Children.Add(ActionButton("Déclarer", () => DeclareFile(folder, name)));
            panel.Children.Add(row);
        }

        return new Border
        {
            Background = Res("Panel2Brush"),
            BorderBrush = Res("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 0, 12),
            Child = panel,
        };
    }

    // ===== Actions =====

    private void CreateFolder_Click(object sender, RoutedEventArgs e) => CreateFolder();

    private void FolderNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CreateFolder();
    }

    private void CreateFolder()
    {
        if (_mission == null) return;
        Run(() =>
        {
            var name = EconomyService.CreateFolder(_mission, FolderNameBox.Text);
            FolderNameBox.Text = "";
            return $"Dossier « {name} » créé et déclaré. Ajoute maintenant les fichiers du mod.";
        });
    }

    private void DeclareFolder(string name)
    {
        if (_mission == null) return;
        Run(() =>
        {
            var count = EconomyService.DeclareFolder(_mission, name);
            return $"Dossier « {name} » déclaré avec {count} fichier(s). Vérifie que le type de chaque fichier est le bon.";
        });
    }

    private void AddFile(CeFolder folder)
    {
        if (_mission == null) return;
        var dialog = new OpenFileDialog
        {
            Title = $"Fichiers à ajouter dans {folder.Name}",
            Filter = "Fichiers XML (*.xml)|*.xml",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;

        var added = new List<string>();
        foreach (var source in dialog.FileNames)
        {
            var name = Path.GetFileName(source);
            if (File.Exists(Path.Combine(folder.FullPath, name)) &&
                !string.Equals(Path.GetDirectoryName(source), folder.FullPath, StringComparison.OrdinalIgnoreCase) &&
                !Confirm($"Le fichier « {name} » existe déjà dans {folder.Name}. Le remplacer ?"))
                continue;

            var window = new CeFileWindow($"AJOUTER {name.ToUpperInvariant()}",
                "Type deviné d'après le contenu du fichier. Change-le si besoin.",
                EconomyService.GuessType(source), askName: false)
            {
                Owner = Window.GetWindow(this),
            };
            if (window.ShowDialog() != true) continue;

            try
            {
                EconomyService.AddFile(_mission, folder.Name, source, window.SelectedType);
                added.Add(name);
            }
            catch (Exception ex)
            {
                ShowStatus($"Impossible d'ajouter « {name} » : {ex.Message}", "WarnBrush");
                Refresh();
                return;
            }
        }

        if (added.Count == 0) return;
        Refresh();
        ShowStatus($"{added.Count} fichier(s) ajouté(s) dans {folder.Name} : {string.Join(", ", added)}.", "CyanBrush");
    }

    private void CreateEmptyFile(CeFolder folder)
    {
        if (_mission == null) return;
        var window = new CeFileWindow("CRÉER UN FICHIER VIDE",
            $"Le fichier sera créé dans {folder.Name}, prêt à être rempli dans l'éditeur.", "types", askName: true)
        {
            Owner = Window.GetWindow(this),
        };
        if (window.ShowDialog() != true) return;

        try
        {
            var name = EconomyService.CreateEmptyFile(_mission, folder.Name, window.FileName, window.SelectedType);
            Refresh();
            EditRequested?.Invoke(Path.Combine(folder.FullPath, name));
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
        }
    }

    private void DeclareFile(CeFolder folder, string name)
    {
        if (_mission == null) return;
        var path = Path.Combine(folder.FullPath, name);
        var window = new CeFileWindow($"DÉCLARER {name.ToUpperInvariant()}", "Type deviné d'après le contenu du fichier.",
            EconomyService.GuessType(path), askName: false)
        {
            Owner = Window.GetWindow(this),
        };
        if (window.ShowDialog() != true) return;
        Run(() =>
        {
            EconomyService.Declare(_mission, folder.Name, name, window.SelectedType);
            return $"« {name} » déclaré comme « {window.SelectedType} ».";
        });
    }

    private void ChangeType(CeFolder folder, CeFile file)
    {
        if (_mission == null) return;
        var window = new CeFileWindow($"TYPE DE {file.Name.ToUpperInvariant()}", "Ce que le serveur doit faire de ce fichier.",
            file.Type, askName: false)
        {
            Owner = Window.GetWindow(this),
        };
        if (window.ShowDialog() != true || window.SelectedType == file.Type) return;
        Run(() =>
        {
            EconomyService.ChangeType(_mission, folder.Name, file.Name, window.SelectedType);
            return $"« {file.Name} » est maintenant de type « {window.SelectedType} ».";
        });
    }

    private void RemoveFile(CeFolder folder, CeFile file)
    {
        if (_mission == null) return;
        var answer = AskRemove($"Retirer « {file.Name} » de {folder.Name} ?");
        if (answer == MessageBoxResult.Cancel) return;
        Run(() =>
        {
            EconomyService.RemoveFile(_mission, folder.Name, file.Name, deleteFromDisk: answer == MessageBoxResult.Yes);
            return answer == MessageBoxResult.Yes ? $"« {file.Name} » retiré et supprimé." : $"« {file.Name} » retiré (le fichier est gardé dans le dossier).";
        });
    }

    private void RemoveFolder(CeFolder folder)
    {
        if (_mission == null) return;
        var answer = AskRemove($"Retirer le dossier « {folder.Name} » et tous ses fichiers de l'économie du serveur ?");
        if (answer == MessageBoxResult.Cancel) return;
        Run(() =>
        {
            EconomyService.RemoveFolder(_mission, folder.Name, deleteFromDisk: answer == MessageBoxResult.Yes);
            return answer == MessageBoxResult.Yes ? $"Dossier « {folder.Name} » retiré et supprimé." : $"Dossier « {folder.Name} » retiré (gardé sur le disque).";
        });
    }

    private void OpenMission_Click(object sender, RoutedEventArgs e)
    {
        if (_mission != null) OpenFolder(_mission);
    }

    private void OpenCore_Click(object sender, RoutedEventArgs e)
    {
        if (_mission != null) EditRequested?.Invoke(EconomyService.CorePath(_mission));
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    // ===== Outils =====

    private void Run(Func<string> action)
    {
        try
        {
            var message = action();
            Refresh();
            ShowStatus(message + (ServerManager.Instance.IsRunning ? " Redémarre le serveur pour appliquer." : ""), "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
        }
    }

    private MessageBoxResult AskRemove(string question) =>
        MessageBox.Show(Window.GetWindow(this)!,
            $"{question}\n\nOui : retirer ET supprimer les fichiers du disque\nNon : retirer seulement (les fichiers restent dans le dossier)",
            "Stryxhost Manager", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    private bool Confirm(string message) =>
        MessageBox.Show(Window.GetWindow(this)!, message, "Stryxhost Manager",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private Border Row(string text, string subtitle, string subtitleBrush, int index, bool indent = false, bool header = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(indent ? 16 : 0, 0, 12, 0) };
        texts.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = header ? (FontFamily)Application.Current.FindResource("Orbitron") : new FontFamily("Segoe UI"),
            FontSize = header ? 13 : 14,
            FontWeight = header ? FontWeights.Normal : FontWeights.Medium,
            Foreground = Res("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        texts.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = Res(subtitleBrush),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        grid.Children.Add(texts);

        var buttons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
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

    private static WrapPanel Buttons(Border row) => (WrapPanel)((Grid)row.Child).Children[1];

    private Button ActionButton(string label, Action onClick, bool warn = false)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)Application.Current.FindResource("SmallButton"),
            Margin = new Thickness(8, 3, 0, 3),
        };
        if (warn)
        {
            button.Foreground = Res("WarnBrush");
            button.BorderBrush = Res("WarnBrush");
        }
        button.Click += (_, _) => onClick();
        return button;
    }

    private void ShowEmpty(string message)
    {
        FoldersPanel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = Res("MutedBrush"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = Res(brushKey);
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
