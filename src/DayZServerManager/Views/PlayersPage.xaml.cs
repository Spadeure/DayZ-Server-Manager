using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class PlayersPage : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private string _tab = "online";
    private bool _busy;

    public PlayersPage()
    {
        InitializeComponent();

        RconService.Instance.StatusChanged += () => Dispatcher.InvokeAsync(UpdateStatus);
        ServerManager.Instance.StatusChanged += () => Dispatcher.InvokeAsync(UpdateStatus);

        _timer.Tick += async (_, _) =>
        {
            if (_tab == "online" && ServerManager.Instance.IsRunning && !_busy) await LoadAsync(quiet: true);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _timer.Start();
            else _timer.Stop();
        };

        OnlineTab.IsChecked = true;
        UpdateStatus();
    }

    /// <summary>Recharge la page (appelé à chaque ouverture de l'onglet).</summary>
    public async void Refresh()
    {
        UpdateStatus();
        await LoadAsync(quiet: false);
    }

    // ===== Connexion =====

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (RconService.Instance.IsConnected)
        {
            RconService.Instance.Disconnect();
            return;
        }
        ShowInfo("Connexion au RCon…", "MutedBrush");
        if (await RconService.Instance.EnsureConnectedAsync())
        {
            ShowInfo("Connecté au RCon.", "CyanBrush");
            await LoadAsync(quiet: true);
        }
        else
        {
            ShowInfo(RconService.Instance.LastError, "WarnBrush");
        }
    }

    private void UpdateStatus()
    {
        bool connected = RconService.Instance.IsConnected;
        bool running = ServerManager.Instance.IsRunning;
        StatusText.Text = connected ? "RCon connecté" : running ? "RCon non connecté" : "Serveur hors ligne";
        StatusText.Foreground = Res(connected ? "CyanBrush" : running ? "WarnBrush" : "MutedBrush");
        StatusPill.Background = Res(connected ? "CyanSoftBrush" : running ? "WarnSoftBrush" : "Panel2Brush");
        ConnectButton.Content = connected ? "Se déconnecter" : "Se connecter";
        ConnectButton.IsEnabled = running || connected;
    }

    // ===== Onglets =====

    private async void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || AddPanel == null) return;
        _tab = tag;
        bool lists = tag is "whitelist" or "priority";
        AddPanel.Visibility = lists ? Visibility.Visible : Visibility.Collapsed;
        WhitelistToggle.Visibility = tag == "whitelist" ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) await LoadAsync(quiet: false);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync(quiet: false);

    private async Task LoadAsync(bool quiet)
    {
        switch (_tab)
        {
            case "online":
                await LoadPlayersAsync(quiet);
                break;
            case "bans":
                await LoadBansAsync();
                break;
            default:
                LoadSteamList();
                break;
        }
    }

    // ===== Joueurs en ligne =====

    private async Task LoadPlayersAsync(bool quiet)
    {
        if (!ServerManager.Instance.IsRunning)
        {
            ShowEmpty("Le serveur n'est pas lancé.");
            OnlineTab.Content = "En ligne";
            return;
        }

        _busy = true;
        try
        {
            if (!quiet) ShowInfo("Récupération des joueurs…", "MutedBrush");
            var players = await RconService.Instance.GetPlayersAsync();
            OnlineTab.Content = $"En ligne ({players.Count})";
            ListPanel.Children.Clear();
            if (players.Count == 0) ShowEmpty("Aucun joueur connecté pour le moment.");
            for (int i = 0; i < players.Count; i++) ListPanel.Children.Add(BuildPlayerRow(players[i], i));
            ShowInfo($"Mis à jour à {DateTime.Now:HH:mm:ss}. Liste actualisée toutes les 10 secondes.", "MutedBrush");
        }
        catch (Exception ex)
        {
            ShowEmpty("Impossible d'afficher les joueurs.");
            ShowInfo(ex.Message, "WarnBrush");
        }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private UIElement BuildPlayerRow(PlayerInfo player, int index)
    {
        var sub = $"#{player.Id} · {player.Ping} ms · {player.Ip}" + (player.InLobby ? " · en connexion…" : "");
        var row = BuildRow(player.Name, sub, index);
        var buttons = (StackPanel)((Grid)row.Child).Children[1];

        buttons.Children.Add(ActionButton("Message", async () =>
        {
            var dialog = new InputDialog($"MESSAGE À {player.Name.ToUpperInvariant()}", "Le message s'affichera en jeu, uniquement pour ce joueur.", code: false)
            {
                Owner = Window.GetWindow(this),
            };
            if (dialog.ShowDialog() != true) return;
            await RunAsync(() => RconService.Instance.SayAsync(player.Id, dialog.Value), $"Message envoyé à {player.Name}.");
        }));

        buttons.Children.Add(ActionButton("Expulser", async () =>
        {
            var dialog = new InputDialog($"EXPULSER {player.Name.ToUpperInvariant()}", "Raison affichée au joueur (facultatif).", code: false)
            {
                Owner = Window.GetWindow(this),
            };
            if (dialog.ShowDialog() != true) return;
            await RunAsync(() => RconService.Instance.KickAsync(player.Id, dialog.Value), $"{player.Name} a été expulsé.");
            await LoadPlayersAsync(quiet: true);
        }));

        buttons.Children.Add(ActionButton("Bannir", async () =>
        {
            var dialog = new BanWindow(player.Name) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            await RunAsync(() => RconService.Instance.BanAsync(player.Id, dialog.Minutes, dialog.Reason), $"{player.Name} a été banni.");
            await LoadPlayersAsync(quiet: true);
        }, warn: true));

        return row;
    }

    // ===== Bannis =====

    private async Task LoadBansAsync()
    {
        if (!ServerManager.Instance.IsRunning)
        {
            ShowEmpty("Lance le serveur pour voir et gérer les bannis (ils sont gérés par BattlEye).");
            return;
        }

        try
        {
            ShowInfo("Récupération des bannis…", "MutedBrush");
            var bans = await RconService.Instance.GetBansAsync();
            ListPanel.Children.Clear();
            if (bans.Count == 0) ShowEmpty("Aucun joueur banni.");
            for (int i = 0; i < bans.Count; i++)
            {
                var ban = bans[i];
                var reason = string.IsNullOrWhiteSpace(ban.Reason) ? "sans raison" : ban.Reason;
                var row = BuildRow(ban.Target, $"{ban.Remaining} · {reason}", i);
                var buttons = (StackPanel)((Grid)row.Child).Children[1];
                buttons.Children.Add(ActionButton("Débannir", async () =>
                {
                    await RunAsync(() => RconService.Instance.RemoveBanAsync(ban.Index), "Joueur débanni.");
                    await LoadBansAsync();
                }));
                ListPanel.Children.Add(row);
            }
            ShowInfo($"{bans.Count} bannissement(s).", "MutedBrush");
        }
        catch (Exception ex)
        {
            ShowEmpty("Impossible d'afficher les bannis.");
            ShowInfo(ex.Message, "WarnBrush");
        }
    }

    // ===== Whitelist et file prioritaire =====

    private string CurrentListPath => _tab == "whitelist" ? PlayerListsService.WhitelistPath : PlayerListsService.PriorityPath;

    private void LoadSteamList()
    {
        if (!DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder))
        {
            ShowEmpty("Installe d'abord le serveur.");
            return;
        }

        try
        {
            if (_tab == "whitelist") WhitelistToggle.IsChecked = PlayerListsService.IsWhitelistEnabled();
            var ids = PlayerListsService.Read(CurrentListPath);
            ListPanel.Children.Clear();
            if (ids.Count == 0) ShowEmpty("La liste est vide.");

            var names = AppSettings.Current.SteamNames;
            for (int i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var title = names.TryGetValue(id, out var name) && name.Length > 0 ? name : id;
                var row = BuildRow(title, title == id ? "SteamID64" : id, i);
                var buttons = (StackPanel)((Grid)row.Child).Children[1];
                buttons.Children.Add(ActionButton("Retirer", () =>
                {
                    SaveSteamList(PlayerListsService.Read(CurrentListPath).Where(x => x != id).ToList());
                    return Task.CompletedTask;
                }));
                ListPanel.Children.Add(row);
            }

            ShowInfo(_tab == "whitelist"
                ? "Les changements de la whitelist sont pris en compte au prochain redémarrage du serveur."
                : "Les joueurs de la file prioritaire passent devant les autres quand le serveur est plein.", "MutedBrush");
        }
        catch (Exception ex)
        {
            ShowInfo($"Erreur : {ex.Message}", "WarnBrush");
        }
    }

    private void AddSteamId_Click(object sender, RoutedEventArgs e)
    {
        var id = SteamIdBox.Text.Trim();
        if (!PlayerListsService.IsValidSteamId(id))
        {
            ShowInfo("SteamID64 invalide : 17 chiffres commençant par 7656. Tu le trouves sur le profil Steam du joueur (ou sur steamid.io).", "WarnBrush");
            return;
        }

        var name = SteamNameBox.Text.Trim();
        if (name.Length > 0) AppSettings.Current.SteamNames[id] = name;
        AppSettings.Current.Save();

        var ids = PlayerListsService.Read(CurrentListPath);
        if (!ids.Contains(id)) ids.Add(id);
        SaveSteamList(ids);
        SteamIdBox.Text = "";
        SteamNameBox.Text = "";
    }

    private void SaveSteamList(List<string> ids)
    {
        try
        {
            if (_tab == "whitelist") PlayerListsService.WriteWhitelist(ids);
            else PlayerListsService.WritePriority(ids);
            LoadSteamList();
        }
        catch (Exception ex)
        {
            ShowInfo($"Impossible d'enregistrer : {ex.Message}", "WarnBrush");
        }
    }

    private void WhitelistToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PlayerListsService.SetWhitelistEnabled(WhitelistToggle.IsChecked == true);
            ShowInfo(WhitelistToggle.IsChecked == true
                ? "Whitelist activée : redémarre le serveur pour l'appliquer. Pense à t'ajouter à la liste !"
                : "Whitelist désactivée : redémarre le serveur pour l'appliquer.", "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowInfo($"Erreur : {ex.Message}", "WarnBrush");
            WhitelistToggle.IsChecked = !WhitelistToggle.IsChecked;
        }
    }

    // ===== Message à tous =====

    private async void Broadcast_Click(object sender, RoutedEventArgs e) => await BroadcastAsync();

    private async void BroadcastBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await BroadcastAsync();
    }

    private async Task BroadcastAsync()
    {
        var message = BroadcastBox.Text.Trim();
        if (message.Length == 0) return;
        if (await RunAsync(() => RconService.Instance.SayAllAsync(message), "Message envoyé à tous les joueurs."))
            BroadcastBox.Text = "";
    }

    // ===== Outils =====

    private async Task<bool> RunAsync(Func<Task> action, string success)
    {
        try
        {
            await action();
            ShowInfo(success, "CyanBrush");
            return true;
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, "WarnBrush");
            return false;
        }
        finally
        {
            UpdateStatus();
        }
    }

    private Border BuildRow(string title, string subtitle, int index)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        texts.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = Res("MutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        grid.Children.Add(texts);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
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

    private Button ActionButton(string text, Func<Task> onClick, bool warn = false)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource("SmallButton"),
            Margin = new Thickness(8, 0, 0, 0),
        };
        if (warn)
        {
            button.Foreground = Res("WarnBrush");
            button.BorderBrush = Res("WarnBrush");
        }
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private void ShowEmpty(string message)
    {
        ListPanel.Children.Clear();
        ListPanel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = Res("MutedBrush"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private void ShowInfo(string message, string brushKey)
    {
        InfoText.Text = message;
        InfoText.Foreground = Res(brushKey);
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
