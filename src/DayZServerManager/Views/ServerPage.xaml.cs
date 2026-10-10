using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

/// <summary>Tableau de bord : état du serveur, ressources, points à surveiller, joueurs et activité.</summary>
public partial class ServerPage : UserControl
{
    private record Alert(string Brush, string Title, string Sub, string? Action = null, string? Target = null);

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _busy;
    private int _ticks;

    // Processeur du serveur
    private int _lastPid;
    private TimeSpan _lastCpuTime;
    private DateTime _lastSample;

    // Joueurs
    private int? _playerCount;
    private int _peak;
    private DateTime _peakDay = DateTime.Today;
    private bool _loadingPlayers;

    // Points à surveiller (les vérifications longues sont gardées 5 minutes)
    private string? _updateVersion;
    private int _economyErrors;
    private bool? _firewallOpen;
    private DateTime _slowChecksAt = DateTime.MinValue;
    private bool _checking;

    /// <summary>Demande à la fenêtre principale d'ouvrir une page (« players », « economy », « install »…).</summary>
    public event Action<string>? NavigateRequested;

    /// <summary>Compteurs du menu : joueurs connectés, fichiers d'économie en erreur.</summary>
    public event Action<int?, int>? BadgesChanged;

    public ServerPage()
    {
        InitializeComponent();

        var manager = ServerManager.Instance;
        manager.Log += message => Dispatcher.InvokeAsync(() => Log(message));
        manager.StatusChanged += () => Dispatcher.InvokeAsync(() =>
        {
            UpdateStatus();
            _ = UpdatePlayersAsync();
        });

        _clock.Tick += (_, _) =>
        {
            _ticks++;
            UpdateUptime();
            if (_ticks % 2 == 0) UpdateResources();
            if (_ticks % 10 == 0) _ = UpdatePlayersAsync();
            if (_ticks % 60 == 0) _ = UpdateAlertsAsync();
        };
        _clock.Start();

        Log("Bienvenue dans Stryxhost Manager, le gestionnaire de serveurs DayZ.");
        manager.AttachToRunning();
        UpdateStatus();
        UpdateResources();
        _ = UpdatePlayersAsync();
        _ = UpdateAlertsAsync();
    }

    /// <summary>Remet à jour toute la page (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh()
    {
        UpdateStatus();
        _ = UpdatePlayersAsync();
        _ = UpdateAlertsAsync();
    }

    /// <summary>Une nouvelle version de l'application est disponible (null : aucune).</summary>
    public void SetUpdateAvailable(string? version)
    {
        _updateVersion = version;
        _ = UpdateAlertsAsync();
    }

    // ===== Actions =====

    private async void Start_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ServerManager.Instance.StartAsync());

    private async void Stop_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ServerManager.Instance.StopAsync());

    private async void Restart_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ServerManager.Instance.RestartAsync());

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        UpdateStatus();
        try { await action(); }
        catch (Exception ex) { Log($"Erreur : {ex.Message}"); }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("install");

    private void AllPlayers_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("players");

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var folder = ServerManager.ProfilesFolder;
        if (!Directory.Exists(folder))
        {
            Log("Le dossier des journaux sera créé au premier démarrage du serveur.");
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    // ===== État =====

    private void UpdateStatus()
    {
        bool installed = DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder);
        bool running = ServerManager.Instance.IsRunning;

        StatusText.Text = !installed ? "NON INSTALLÉ" : running ? "EN LIGNE" : "HORS LIGNE";
        var statusBrush = Res(running ? "CyanBrush" : installed ? "MutedBrush" : "WarnBrush");
        StatusText.Foreground = statusBrush;
        StatusDot.Fill = statusBrush;
        StatusDot.Effect = running
            ? new DropShadowEffect { Color = Color.FromRgb(0x2F, 0xE3, 0xF2), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 }
            : null;

        StartButton.Visibility = installed && !running ? Visibility.Visible : Visibility.Collapsed;
        RestartButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = RestartButton.IsEnabled = StopButton.IsEnabled = !_busy;

        SubtitleText.Text = BuildSubtitle(installed);
        UpdateUptime();
    }

    private static string BuildSubtitle(bool installed)
    {
        if (!installed) return "Installe le serveur pour commencer.";
        var parts = new List<string>();
        try
        {
            var path = Path.Combine(ServerManager.ServerFolder, "serverDZ.cfg");
            if (File.Exists(path))
            {
                var config = ServerConfigFile.Load(path);
                var name = config.Get("hostname");
                if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
                var template = config.Get("template");
                if (!string.IsNullOrWhiteSpace(template)) parts.Add(BackupService.MapName(template));
            }
        }
        catch
        {
            // Configuration illisible : sous-titre réduit.
        }
        parts.Add(AppSettings.Current.ServerBranch == "experimental" ? "version expérimentale" : "version stable");
        return string.Join(" · ", parts);
    }

    private void UpdateUptime()
    {
        var manager = ServerManager.Instance;
        if (manager.StartedAt == null || !manager.IsRunning)
        {
            UptimeText.Text = DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder)
                ? "Le serveur n'est pas lancé."
                : "Assistant d'installation disponible.";
            return;
        }

        var up = DateTime.Now - manager.StartedAt.Value;
        var text = $"Depuis {(int)up.TotalHours} h {up.Minutes:00}";
        var settings = AppSettings.Current;
        if (settings.ScheduledRestart && settings.RestartHours > 0)
            text += $" · redémarrage à {manager.StartedAt.Value.AddHours(settings.RestartHours):HH:mm}";
        UptimeText.Text = text;
    }

    // ===== Ressources (uniquement ce que le serveur utilise) =====

    private void UpdateResources()
    {
        var process = ServerManager.Instance.ServerProcess;
        bool running = process != null && ServerManager.Instance.IsRunning;
        double cpuPercent = 0;
        long ramBytes = 0;
        bool cpuReady = false;

        if (running)
        {
            try
            {
                process!.Refresh();
                var now = DateTime.UtcNow;
                var cpuTime = process.TotalProcessorTime;
                if (_lastPid == process.Id && _lastSample != default)
                {
                    var elapsed = (now - _lastSample).TotalMilliseconds * Environment.ProcessorCount;
                    cpuPercent = elapsed > 0 ? Math.Clamp((cpuTime - _lastCpuTime).TotalMilliseconds / elapsed * 100, 0, 100) : 0;
                    cpuReady = true;
                }
                _lastPid = process.Id;
                _lastCpuTime = cpuTime;
                _lastSample = now;
                ramBytes = process.WorkingSet64;
            }
            catch
            {
                running = false; // le serveur vient de s'arrêter
            }
        }
        if (!running) _lastSample = default;

        CpuValue.Text = !running ? "—" : cpuReady ? $"{cpuPercent:0} %" : "…";
        CpuBar.Value = running ? cpuPercent : 0;
        CpuInfo.Text = running ? $"Utilisé par le serveur · {Environment.ProcessorCount} cœurs" : "Serveur hors ligne";

        var totalRam = SystemMetrics.GetMemory()?.Total ?? 0;
        RamValue.Text = running ? FormatBytes(ramBytes) : "—";
        RamBar.Value = running && totalRam > 0 ? ramBytes * 100.0 / totalRam : 0;
        RamInfo.Text = !running
            ? "Serveur hors ligne"
            : totalRam > 0 ? $"{ramBytes * 100.0 / totalRam:0} % des {FormatBytes((long)totalRam)} du PC" : "";
    }

    // ===== Joueurs =====

    private async Task UpdatePlayersAsync()
    {
        if (_loadingPlayers) return;
        _loadingPlayers = true;
        try
        {
            if (DateTime.Today != _peakDay)
            {
                _peakDay = DateTime.Today;
                _peak = 0;
            }

            if (!ServerManager.Instance.IsRunning)
            {
                _playerCount = null;
                PlayersValue.Text = "—";
                PlayersBar.Value = 0;
                PlayersSub.Text = "Serveur hors ligne";
                ShowPlayersMessage("Le serveur n'est pas lancé.");
                BadgesChanged?.Invoke(null, _economyErrors);
                return;
            }

            // Nombre de joueurs : la même information que la liste des serveurs du jeu.
            var info = await ServerQuery.QueryAsync(AppSettings.Current.QueryPort);
            if (info != null)
            {
                _playerCount = info.Players;
                _peak = Math.Max(_peak, info.Players);
                PlayersValue.Text = $"{info.Players} / {info.MaxPlayers}";
                PlayersBar.Value = info.MaxPlayers > 0 ? info.Players * 100.0 / info.MaxPlayers : 0;
                PlayersSub.Text = $"Pic du jour : {_peak}";
            }
            else
            {
                _playerCount = null;
                PlayersValue.Text = "…";
                PlayersBar.Value = 0;
                PlayersSub.Text = "Serveur en cours de démarrage";
            }
            BadgesChanged?.Invoke(_playerCount, _economyErrors);

            // Liste détaillée (RCon), seulement quand la page est affichée.
            if (!IsVisible) return;
            if (string.IsNullOrWhiteSpace(BattlEyeConfig.ReadPassword()))
            {
                ShowPlayersMessage("Ajoute un mot de passe RCon dans Configuration pour voir la liste des joueurs.");
                return;
            }
            try
            {
                var players = await RconService.Instance.GetPlayersAsync();
                BuildPlayers(players);
            }
            catch
            {
                ShowPlayersMessage(info == null
                    ? "Liste disponible une fois le serveur démarré (1 à 2 minutes)."
                    : $"Liste des joueurs indisponible : {RconService.Instance.LastError}");
            }
        }
        finally
        {
            _loadingPlayers = false;
        }
    }

    private void BuildPlayers(List<PlayerInfo> players)
    {
        PlayersPanel.Children.Clear();
        if (players.Count == 0)
        {
            ShowPlayersMessage("Aucun joueur connecté pour le moment.");
            return;
        }

        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = Res("Panel2Brush"),
                Child = new TextBlock
                {
                    Text = player.Name.Length > 0 ? player.Name[..1].ToUpperInvariant() : "?",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Res("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });

            var name = new TextBlock
            {
                Text = player.InLobby ? $"{player.Name} (connexion…)" : player.Name,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = Res("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var ping = new TextBlock
            {
                Text = $"{player.Ping} ms",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = Res(player.Ping > 120 ? "WarnBrush" : "CyanBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(ping, 2);
            grid.Children.Add(ping);

            PlayersPanel.Children.Add(Row(grid, i));
        }
    }

    private void ShowPlayersMessage(string message)
    {
        PlayersPanel.Children.Clear();
        PlayersPanel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = Res("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
    }

    // ===== Points à surveiller =====

    private async Task UpdateAlertsAsync()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var settings = AppSettings.Current;
            bool installed = DependencyChecker.IsServerInstalled(settings.ServerFolder);

            // Vérifications plus longues (fichiers d'économie, pare-feu) : toutes les 5 minutes.
            if (installed && DateTime.Now - _slowChecksAt > TimeSpan.FromMinutes(5))
            {
                _slowChecksAt = DateTime.Now;
                _economyErrors = await Task.Run(() => CountEconomyErrors());
                try
                {
                    var status = await FirewallService.GetStatusAsync();
                    _firewallOpen = status.Values.All(open => open);
                }
                catch
                {
                    _firewallOpen = null;
                }
                BadgesChanged?.Invoke(_playerCount, _economyErrors);
            }

            var alerts = new List<Alert>();
            if (!installed)
            {
                alerts.Add(new Alert("WarnBrush", "Le serveur n'est pas installé",
                    "L'assistant télécharge SteamCMD, les fichiers du serveur et ouvre le pare-feu.", "Installer", "install"));
            }
            else
            {
                if (_economyErrors > 0)
                    alerts.Add(new Alert("WarnBrush", $"{_economyErrors} fichier(s) d'économie en erreur",
                        "Le serveur risque de ne pas faire apparaître ces objets.", "Corriger", "economy"));

                if (_firewallOpen == false)
                    alerts.Add(new Alert("WarnBrush", "Ports fermés dans le pare-feu Windows",
                        "Les joueurs ne pourront pas rejoindre le serveur.", "Ouvrir", "firewall"));

                if (string.IsNullOrWhiteSpace(BattlEyeConfig.ReadPassword()))
                    alerts.Add(new Alert("WarnBrush", "Aucun mot de passe RCon",
                        "Nécessaire pour la liste des joueurs et les avertissements avant redémarrage.", "Régler", "config"));

                var drive = FreeSpace(settings.ServerFolder);
                if (drive is < 10L * 1_073_741_824)
                    alerts.Add(new Alert("WarnBrush", "Peu d'espace disque",
                        $"{FormatBytes(drive.Value)} libres : les sauvegardes et mises à jour risquent d'échouer.", "Sauvegardes", "backups"));
            }

            if (_updateVersion != null)
                alerts.Add(new Alert("AccentBrush", $"Version {_updateVersion} de l'application disponible",
                    "Les nouveautés sont dans le patch note.", "Voir", "updates"));

            if (installed)
            {
                BackupInfo? last = null;
                try { last = await Task.Run(() => BackupService.List().FirstOrDefault()); }
                catch { /* dossier illisible */ }
                alerts.Add(last == null
                    ? new Alert("AccentBrush", "Aucune sauvegarde pour l'instant",
                        settings.AutoBackup ? "Une sauvegarde sera faite au prochain démarrage du serveur." : "La sauvegarde automatique est désactivée.",
                        "Sauvegardes", "backups")
                    : new Alert("CyanBrush", $"Dernière sauvegarde : {last.Date:dd/MM à HH:mm}",
                        $"{last.Map} · {last.ReasonLabel} · {FormatBytes(last.Size)}", "Ouvrir", "backups"));
            }

            if (installed && alerts.All(a => a.Brush != "WarnBrush"))
                alerts.Insert(0, new Alert("CyanBrush", "Tout est en ordre", "Aucun problème détecté sur le serveur."));

            BuildAlerts(alerts.Take(5).ToList());
        }
        finally
        {
            _checking = false;
        }
    }

    private static int CountEconomyErrors()
    {
        try
        {
            var mission = EconomyService.MissionFolder();
            if (mission == null) return 0;
            return EconomyService.ReadFolders(mission).SelectMany(f => f.Files).Count(f => !EconomyService.Check(f).Ok);
        }
        catch
        {
            return 0;
        }
    }

    private static long? FreeSpace(string folder)
    {
        try { return new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace; }
        catch { return null; }
    }

    private void BuildAlerts(List<Alert> alerts)
    {
        AlertsPanel.Children.Clear();
        for (int i = 0; i < alerts.Count; i++)
        {
            var alert = alerts[i];
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brush = Res(alert.Brush);
            grid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = brush,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = ((SolidColorBrush)brush).Color, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 },
            });

            var texts = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = alert.Title,
                FontSize = 13.5,
                FontWeight = FontWeights.Medium,
                Foreground = Res("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            texts.Children.Add(new TextBlock
            {
                Text = alert.Sub,
                FontSize = 12,
                Foreground = Res("MutedBrush"),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            if (alert.Action != null && alert.Target != null)
            {
                var target = alert.Target;
                var button = new Button
                {
                    Content = alert.Action,
                    Style = (Style)Application.Current.FindResource("SmallButton"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                button.Click += (_, _) => NavigateRequested?.Invoke(target);
                Grid.SetColumn(button, 2);
                grid.Children.Add(button);
            }

            AlertsPanel.Children.Add(Row(grid, i));
        }
    }

    // ===== Outils =====

    private Border Row(UIElement content, int index) => new()
    {
        BorderBrush = Res("LineBrush"),
        BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0),
        Padding = new Thickness(0, 9, 0, 9),
        Child = content,
    };

    private static string FormatBytes(long bytes) =>
        bytes >= 1_099_511_627_776 ? $"{bytes / 1_099_511_627_776.0:0.0} To"
        : bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.0} Go"
        : $"{bytes / 1_048_576.0:0} Mo";

    private void Log(string message)
    {
        LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        LogScroll.ScrollToEnd();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
