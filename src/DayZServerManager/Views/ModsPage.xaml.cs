using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class ModsPage : UserControl
{
    private bool _busy;
    private bool _searching;
    private bool _popularLoaded;
    private List<WorkshopItem> _results = new();
    private readonly Dictionary<string, string> _knownTitles = new();

    public ModsPage()
    {
        InitializeComponent();
        ResultsTab.IsChecked = true;
        BuildList();
    }

    /// <summary>Remet à jour la page (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh()
    {
        BuildList();
        BuildResults();
        if (!_popularLoaded)
        {
            _popularLoaded = true;
            _ = SearchAsync("");
        }
    }

    // ===== Recherche =====

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync(SearchBox.Text);

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync(SearchBox.Text);
    }

    private async Task SearchAsync(string query)
    {
        if (_searching) return;
        _searching = true;
        SearchButton.IsEnabled = false;
        ResultsTab.IsChecked = true;
        ShowStatus(string.IsNullOrWhiteSpace(query) ? "Chargement des mods populaires…" : "Recherche en cours…", "MutedBrush");

        try
        {
            // Un lien ou un numéro de mod : on affiche directement ce mod.
            var directId = query.Contains("id=") || query.Trim().All(char.IsDigit) ? ModService.ParseWorkshopId(query) : null;
            _results = directId != null
                ? await WorkshopService.GetDetailsAsync([directId])
                : await WorkshopService.SearchAsync(query);

            foreach (var item in _results) _knownTitles[item.Id] = item.Title;

            if (_results.Count == 0)
                ShowStatus("Aucun mod trouvé. Essaie un autre nom.", "WarnBrush");
            else if (string.IsNullOrWhiteSpace(query))
                ShowStatus("Les mods DayZ les plus populaires. Tape un nom pour chercher.", "MutedBrush");
            else
                ShowStatus($"{_results.Count} mod(s) trouvé(s).", "MutedBrush");
        }
        catch (Exception ex)
        {
            _results = new();
            ShowStatus($"Recherche impossible : {ex.Message}", "WarnBrush");
        }
        finally
        {
            _searching = false;
            SearchButton.IsEnabled = true;
            BuildResults();
        }
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (ResultsScroll == null || InstalledScroll == null || ActionsPanel == null) return;
        bool showResults = ResultsTab.IsChecked == true;
        ResultsScroll.Visibility = showResults ? Visibility.Visible : Visibility.Collapsed;
        InstalledScroll.Visibility = showResults ? Visibility.Collapsed : Visibility.Visible;
        ActionsPanel.Visibility = showResults ? Visibility.Collapsed : Visibility.Visible;
    }

    // ===== Actions =====

    private async Task InstallAsync(WorkshopItem item)
    {
        if (_busy) return;
        if (AppSettings.Current.Mods.Any(m => m.Id == item.Id))
        {
            ShowStatus($"« {item.Title} » est déjà installé.", "MutedBrush");
            return;
        }

        // Mods requis (par exemple Community Framework) : on propose de les installer avant.
        var ids = new List<string> { item.Id };
        try
        {
            ShowStatus($"Recherche des mods requis par « {item.Title} »…", "MutedBrush");
            var required = await WorkshopService.GetRequiredItemsAsync(item.Id);
            var missing = required.Where(id => AppSettings.Current.Mods.All(m => m.Id != id)).ToList();
            if (missing.Count > 0)
            {
                var details = await WorkshopService.GetDetailsAsync(missing);
                foreach (var detail in details) _knownTitles[detail.Id] = detail.Title;
                var names = missing.Select(id => _knownTitles.TryGetValue(id, out var title) ? title : id);

                var answer = MessageBox.Show(Window.GetWindow(this)!,
                    $"« {item.Title} » a besoin de ces mods pour fonctionner :\n\n• {string.Join("\n• ", names)}\n\n" +
                    "Les installer aussi (ils seront chargés avant lui) ?\n\nOui : tout installer   Non : installer seulement ce mod",
                    "Stryxhost Manager", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.Yes) ids.InsertRange(0, missing);
            }
        }
        catch
        {
            // Page du Workshop injoignable : on installe au moins le mod demandé.
            Log("Impossible de vérifier les mods requis, installation du mod seul.");
        }

        await DownloadAndInstallAsync(ids);
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
        new ShareModsWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Detect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder))
        {
            ShowStatus("Installe d'abord le serveur (Paramètres → Ouvrir l'installation).", "WarnBrush");
            return;
        }
        try
        {
            var found = ModService.DetectManualMods(ServerManager.ServerFolder, AppSettings.Current.Mods);
            AppSettings.Current.Mods.AddRange(found);
            AppSettings.Current.Save();
            BuildList();
            ShowStatus(found.Count == 0
                ? "Aucun nouveau mod trouvé dans le dossier du serveur."
                : $"{found.Count} mod(s) ajouté(s) : {string.Join(", ", found.Select(m => m.Name))}. Vérifie leur ordre de chargement.",
                found.Count == 0 ? "MutedBrush" : "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
        }
    }

    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        // Les mods ajoutés à la main n'ont pas de numéro Workshop : on ne peut pas les mettre à jour.
        var ids = AppSettings.Current.Mods.Where(m => m.Id.Length > 0).Select(m => m.Id).ToList();
        if (ids.Count == 0)
        {
            ShowStatus("Aucun mod à mettre à jour.", "MutedBrush");
            return;
        }
        await DownloadAndInstallAsync(ids);
    }

    private async Task DownloadAndInstallAsync(List<string> ids)
    {
        var mainFolder = AppSettings.Current.ServerFolder;
        if (!DependencyChecker.IsServerInstalled(mainFolder))
        {
            ShowStatus("Installe d'abord le serveur (Paramètres → Ouvrir l'installation).", "WarnBrush");
            return;
        }

        var steam = new SteamCmdService(mainFolder);
        if (!File.Exists(steam.SteamCmdExe))
        {
            ShowStatus("SteamCMD est introuvable. Relance l'installation du serveur.", "WarnBrush");
            return;
        }

        // Il faut un compte Steam qui possède DayZ. Le mot de passe n'est demandé que si besoin.
        var user = AppSettings.Current.SteamUser;
        string? password = null;
        if (string.IsNullOrWhiteSpace(user))
        {
            var login = new SteamLoginWindow(null) { Owner = Window.GetWindow(this) };
            if (login.ShowDialog() != true) return;
            user = login.UserName;
            password = login.Password;
            AppSettings.Current.SteamUser = user;
            AppSettings.Current.Save();
        }

        steam.Output += line => Dispatcher.Invoke(() => Log(line));
        steam.AskPassword = () => Dispatcher.Invoke(() =>
        {
            var login = new SteamLoginWindow(user) { Owner = Window.GetWindow(this) };
            return login.ShowDialog() == true ? login.Password : null;
        });
        steam.AskSteamGuardCode = () => Dispatcher.Invoke(() =>
        {
            var dialog = new InputDialog("CODE STEAM GUARD",
                "Ouvre l'application Steam sur ton téléphone, va dans Steam Guard et entre le code affiché.")
            {
                Owner = Window.GetWindow(this),
            };
            return dialog.ShowDialog() == true ? dialog.Value : null;
        });

        SetBusy(true);
        ShowStatus($"Téléchargement de {ids.Count} mod(s) en cours… (les gros mods peuvent prendre plusieurs minutes)", "MutedBrush");
        var downloaded = new List<string>();

        try
        {
            // SteamCMD échoue parfois sur les gros mods (délai dépassé) : on réessaie jusqu'à 3 fois.
            var pending = new List<string>(ids);
            for (int attempt = 1; attempt <= 3 && pending.Count > 0; attempt++)
            {
                if (attempt > 1) Log($"Nouvel essai ({attempt}/3) pour {pending.Count} mod(s)…");
                var result = await steam.DownloadWorkshopItemsAsync(user, password, pending, CancellationToken.None);
                password = null; // ensuite, SteamCMD réutilise la connexion enregistrée

                var succeeded = pending.Where(id => result.Output.Contains($"Downloaded item {id}")).ToList();
                downloaded.AddRange(succeeded);
                pending = pending.Except(succeeded).ToList();
            }

            var serverFolder = ServerManager.ServerFolder;
            foreach (var id in downloaded)
            {
                var source = Path.Combine(steam.WorkshopContentFolder, id);
                var existing = AppSettings.Current.Mods.FirstOrDefault(m => m.Id == id);
                var fallback = existing?.Name ?? (_knownTitles.TryGetValue(id, out var title) ? title : id);
                var name = ModService.ReadModName(source, fallback);
                var folderName = existing?.Folder ?? UniqueFolderName(ModService.MakeFolderName(name, id), id);

                var entry = existing ?? new ModEntry { Id = id, Name = name, Folder = folderName };
                entry.Name = name;
                Log($"Vérification de « {name} »…");
                var copied = await Task.Run(() => ModService.SyncMod(entry, source, serverFolder));
                if (existing == null) AppSettings.Current.Mods.Add(entry);
                Log(copied ? $"« {name} » copié dans le serveur ({folderName})." : $"« {name} » est déjà à jour.");
            }
            AppSettings.Current.Save();

            var failed = ids.Except(downloaded).ToList();
            if (failed.Count > 0)
                ShowStatus($"Échec pour : {string.Join(", ", failed)}. Vérifie le numéro du mod et regarde le journal.", "WarnBrush");
            else
                ShowStatus(ServerManager.Instance.IsRunning
                    ? "Mods installés ! Redémarre le serveur pour les charger."
                    : "Mods installés !", "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
            AppSettings.Current.Save();
        }
        finally
        {
            SetBusy(false);
            BuildList();
            BuildResults();
        }
    }

    private static string UniqueFolderName(string folderName, string id) =>
        AppSettings.Current.Mods.Any(m => string.Equals(m.Folder, folderName, StringComparison.OrdinalIgnoreCase))
            ? $"{folderName}_{id}"
            : folderName;

    private void Move(ModEntry mod, int offset)
    {
        var mods = AppSettings.Current.Mods;
        int index = mods.IndexOf(mod);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= mods.Count) return;
        mods.RemoveAt(index);
        mods.Insert(target, mod);
        AppSettings.Current.Save();
        BuildList();
    }

    private async void Remove(ModEntry mod)
    {
        if (_busy) return;
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            $"Supprimer le mod « {mod.Name} » du serveur ?\n\nSon dossier {mod.Folder} sera supprimé du serveur.", "Stryxhost Manager",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            await Task.Run(() => ModService.RemoveFromServer(ServerManager.ServerFolder, mod.Folder));
            AppSettings.Current.Mods.Remove(mod);
            AppSettings.Current.Save();
            Log($"« {mod.Name} » a été supprimé.");
            ShowStatus("Mod supprimé.", "MutedBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Impossible de supprimer : {ex.Message} (le serveur est peut-être lancé).", "WarnBrush");
        }
        finally
        {
            SetBusy(false);
            BuildList();
        }
    }

    // ===== Liste =====

    private void BuildList()
    {
        var mods = AppSettings.Current.Mods;
        InstalledTab.Content = $"Installés ({mods.Count})";
        ModsList.Children.Clear();

        if (mods.Count == 0)
        {
            ModsList.Children.Add(new TextBlock
            {
                Text = "Aucun mod installé pour l'instant.",
                FontSize = 13,
                Foreground = Res("MutedBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        for (int i = 0; i < mods.Count; i++)
            ModsList.Children.Add(BuildRow(mods[i], i, mods.Count));
    }

    private void BuildResults()
    {
        ResultsList.Children.Clear();
        for (int i = 0; i < _results.Count; i++)
            ResultsList.Children.Add(BuildResultRow(_results[i], i));
    }

    private UIElement BuildResultRow(WorkshopItem item, int index)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Image du mod (chargée en arrière-plan).
        var preview = new Border
        {
            Width = 112,
            Height = 63,
            CornerRadius = new CornerRadius(8),
            Background = Res("Panel2Brush"),
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (Uri.TryCreate(item.PreviewUrl, UriKind.Absolute, out var imageUri))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = imageUri;
                bitmap.DecodePixelWidth = 224;
                bitmap.EndInit();
                preview.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            }
            catch
            {
                // Image indisponible : on garde le fond uni.
            }
        }
        grid.Children.Add(preview);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        texts.Children.Add(new TextBlock
        {
            Text = $"{FormatCount(item.Subscriptions)} abonnés · {FormatSize(item.FileSize)} · mis à jour le {item.Updated:dd/MM/yyyy}",
            FontSize = 12,
            Foreground = Res("MutedBrush"),
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var view = new Button
        {
            Content = "Voir",
            Style = (Style)Application.Current.FindResource("SmallButton"),
            Margin = new Thickness(12, 0, 0, 0),
            ToolTip = "Ouvrir la page du mod sur le Steam Workshop",
        };
        view.Click += (_, _) => Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
        buttons.Children.Add(view);

        bool installed = AppSettings.Current.Mods.Any(m => m.Id == item.Id);
        var install = new Button
        {
            Content = installed ? "Installé ✓" : "Installer",
            Style = (Style)Application.Current.FindResource(installed ? "SmallButton" : "PrimaryButton"),
            Height = 32,
            Padding = new Thickness(14, 0, 14, 0),
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = !installed && !_busy,
        };
        install.Click += async (_, _) => await InstallAsync(item);
        buttons.Children.Add(install);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        return new Border
        {
            BorderBrush = Res("LineBrush"),
            BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 10),
            Child = grid,
        };
    }

    private static string FormatCount(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:0.#} M"
        : value >= 1_000 ? $"{value / 1_000.0:0.#} k"
        : value.ToString();

    private static string FormatSize(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.#} Go"
        : bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:0} Mo"
        : $"{Math.Max(1, bytes / 1024)} Ko";

    private UIElement BuildRow(ModEntry mod, int index, int count)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Ordre de chargement
        var order = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        order.Children.Add(SmallButton("▲", index > 0, () => Move(mod, -1)));
        order.Children.Add(SmallButton("▼", index < count - 1, () => Move(mod, 1)));
        grid.Children.Add(order);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = mod.Name,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = Res(mod.Enabled ? "TextBrush" : "MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        texts.Children.Add(new TextBlock
        {
            Text = mod.Id.Length > 0 ? $"{mod.Folder} · {mod.Id}" : $"{mod.Folder} · ajouté à la main",
            FontSize = 12,
            Foreground = Res("MutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var serverSide = new CheckBox
        {
            Content = "Serveur",
            FontSize = 12,
            IsChecked = mod.ServerSide,
            Style = (Style)Application.Current.FindResource("ToggleSwitch"),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Mod chargé uniquement par le serveur (les joueurs n'ont pas besoin de le télécharger)",
        };
        serverSide.Click += (_, _) =>
        {
            mod.ServerSide = serverSide.IsChecked == true;
            AppSettings.Current.Save();
        };
        Grid.SetColumn(serverSide, 2);
        grid.Children.Add(serverSide);

        var enabled = new CheckBox
        {
            Content = "Actif",
            FontSize = 12,
            IsChecked = mod.Enabled,
            Style = (Style)Application.Current.FindResource("ToggleSwitch"),
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        enabled.Click += (_, _) =>
        {
            mod.Enabled = enabled.IsChecked == true;
            AppSettings.Current.Save();
            BuildList();
        };
        Grid.SetColumn(enabled, 3);
        grid.Children.Add(enabled);

        var remove = new Button
        {
            Content = "Supprimer",
            Style = (Style)Application.Current.FindResource("SmallButton"),
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !_busy,
        };
        remove.Click += (_, _) => Remove(mod);
        Grid.SetColumn(remove, 4);
        grid.Children.Add(remove);

        return new Border
        {
            BorderBrush = Res("LineBrush"),
            BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 10),
            Child = grid,
        };
    }

    private Button SmallButton(string text, bool enabled, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 9,
            Width = 26,
            Height = 20,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.FindResource("SecondaryButton"),
            IsEnabled = enabled && !_busy,
            Margin = new Thickness(0, 1, 0, 1),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    // ===== Outils =====

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SearchButton.IsEnabled = !busy && !_searching;
        UpdateAllButton.IsEnabled = !busy;
        BuildList();
        BuildResults();
    }

    private void Log(string message)
    {
        LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        LogScroll.ScrollToEnd();
    }

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = Res(brushKey);
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
