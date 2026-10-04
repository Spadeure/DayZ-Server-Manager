using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class ModsPage : UserControl
{
    private bool _busy;

    public ModsPage()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>Remet à jour la liste (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh() => BuildList();

    // ===== Actions =====

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var id = ModService.ParseWorkshopId(ModInput.Text);
        if (id == null)
        {
            ShowStatus("Lien ou numéro de mod invalide.", "WarnBrush");
            return;
        }
        if (AppSettings.Current.Mods.Any(m => m.Id == id))
        {
            ShowStatus("Ce mod est déjà dans la liste. Utilise « Mettre à jour tous les mods » pour le mettre à jour.", "WarnBrush");
            return;
        }

        await DownloadAndInstallAsync([id]);
        ModInput.Text = "";
    }

    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var ids = AppSettings.Current.Mods.Select(m => m.Id).ToList();
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
            ShowStatus("Installe d'abord le serveur depuis l'onglet Installation.", "WarnBrush");
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
                var name = ModService.ReadModName(source, existing?.Name ?? id);
                var folderName = existing?.Folder ?? UniqueFolderName(ModService.MakeFolderName(name, id), id);

                Log($"Copie de « {name} » dans le serveur ({folderName})…");
                await Task.Run(() => ModService.InstallToServer(source, serverFolder, folderName));

                if (existing == null)
                    AppSettings.Current.Mods.Add(new ModEntry { Id = id, Name = name, Folder = folderName });
                else
                    existing.Name = name;
                Log($"« {name} » est prêt.");
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
            $"Supprimer le mod « {mod.Name} » du serveur ?", "DayZ Server Manager",
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
        ListTitle.Text = $"MODS INSTALLÉS ({mods.Count})";
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
            Text = $"{mod.Folder} · {mod.Id}",
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
        AddButton.IsEnabled = !busy;
        UpdateAllButton.IsEnabled = !busy;
        BuildList();
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
