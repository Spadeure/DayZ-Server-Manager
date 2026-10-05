using System.Diagnostics;
using System.IO;

namespace DayZServerManager.Services;

/// <summary>Démarre, arrête et surveille le serveur DayZ (redémarrage en cas de crash et programmé).</summary>
public sealed class ServerManager
{
    public static ServerManager Instance { get; } = new();

    private Process? _process;
    private bool _stopRequested;
    private bool _restarting;
    private readonly HashSet<int> _warningsSent = new();
    private bool _updatePending;
    private bool _checkingUpdate;
    private DateTime _lastUpdateCheck = DateTime.Now;
    private static readonly int[] WarningMinutes = [1, 5, 10];
    private readonly System.Threading.Timer _scheduleTimer;

    private ServerManager()
    {
        _scheduleTimer = new System.Threading.Timer(_ =>
        {
            CheckScheduledRestart();
            _ = CheckServerUpdateAsync();
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public DateTime? StartedAt { get; private set; }

    /// <summary>Le processus du serveur (pour lire son utilisation du processeur et de la mémoire).</summary>
    public Process? ServerProcess => _process;

    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch { return false; }
        }
    }

    /// <summary>Le serveur a démarré ou s'est arrêté (appelé depuis n'importe quel fil).</summary>
    public event Action? StatusChanged;

    /// <summary>Message à afficher dans le journal (appelé depuis n'importe quel fil).</summary>
    public event Action<string>? Log;

    public static string ServerFolder => DependencyChecker.ServerFolder(AppSettings.Current.ServerFolder);
    public static string ExePath => Path.Combine(ServerFolder, "DayZServer_x64.exe");
    public static string ProfilesFolder => Path.Combine(ServerFolder, "profiles");

    /// <summary>Ligne de lancement réellement utilisée (personnalisée, ou automatique + paramètres en plus).</summary>
    public static string BuildArguments()
    {
        var s = AppSettings.Current;
        if (s.UseCustomLaunchLine && !string.IsNullOrWhiteSpace(s.CustomLaunchLine)) return s.CustomLaunchLine.Trim();
        var extra = (s.ExtraLaunchArgs ?? "").Trim();
        return extra.Length == 0 ? BuildDefaultArguments() : $"{BuildDefaultArguments()} {extra}";
    }

    /// <summary>Ligne automatique : ports, journaux et mods de l'onglet Mods.</summary>
    public static string BuildDefaultArguments()
    {
        var s = AppSettings.Current;
        var args = new List<string>
        {
            "-config=serverDZ.cfg", $"-port={s.GamePort}", "-profiles=profiles",
            "-dologs", "-adminlog", "-netlog", "-freezecheck",
        };

        var mods = s.Mods.Where(m => m.Enabled && !m.ServerSide).Select(m => m.Folder).ToList();
        var serverMods = s.Mods.Where(m => m.Enabled && m.ServerSide).Select(m => m.Folder).ToList();
        if (mods.Count > 0) args.Add($"\"-mod={string.Join(';', mods)}\"");
        if (serverMods.Count > 0) args.Add($"\"-serverMod={string.Join(';', serverMods)}\"");
        return string.Join(' ', args);
    }

    /// <summary>Reprend le contrôle d'un serveur déjà lancé (par exemple après un redémarrage de l'application).</summary>
    public void AttachToRunning()
    {
        if (IsRunning || !DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder)) return;

        foreach (var process in Process.GetProcessesByName("DayZServer_x64"))
        {
            try
            {
                if (!string.Equals(process.MainModule?.FileName, ExePath, StringComparison.OrdinalIgnoreCase)) continue;
                process.EnableRaisingEvents = true;
                process.Exited += OnExited;
                _process = process;
                StartedAt = process.StartTime;
                _stopRequested = false;
                Log?.Invoke("Serveur déjà en cours d'exécution détecté.");
                StatusChanged?.Invoke();
                return;
            }
            catch
            {
                // Processus inaccessible : on l'ignore.
            }
        }
    }

    /// <summary>Démarre le serveur, avec sauvegarde automatique et nettoyage des vieux journaux avant.</summary>
    public async Task<bool> StartAsync()
    {
        if (IsRunning) return true;
        var settings = AppSettings.Current;

        if (settings.AutoBackup && DependencyChecker.IsServerInstalled(settings.ServerFolder))
        {
            try
            {
                var backup = await Task.Run(() => BackupService.Create("auto"));
                if (backup != null) Log?.Invoke($"Sauvegarde automatique créée ({backup.FileName}).");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Sauvegarde automatique impossible : {ex.Message}");
            }
        }

        if (settings.AutoCleanLogs)
        {
            try
            {
                var (count, _) = await Task.Run(() => LogService.CleanOldLogs());
                if (count > 0) Log?.Invoke($"{count} vieux journal(aux) supprimé(s).");
            }
            catch
            {
                // Nettoyage non essentiel.
            }
        }

        if (settings.AutoUpdateServer || (settings.AutoUpdateMods && settings.Mods.Any(m => m.Id.Length > 0)))
            await RunUpdatesBeforeStartAsync(settings);

        // BattlEye renomme son fichier pendant que le serveur tourne : on le réécrit avant chaque démarrage.
        try { BattlEyeConfig.Write(BattlEyeConfig.ReadPassword(), settings.RconPort); }
        catch { /* le serveur démarrera avec l'ancien fichier */ }

        var started = Start();
        if (started)
        {
            _warningsSent.Clear();
            _lastUpdateCheck = DateTime.Now;
            _ = DiscordNotifier.NotifyAsync("🟢 Serveur démarré", "Le serveur est en cours de démarrage.", DiscordNotifier.Green);
        }
        return started;
    }

    private bool Start()
    {
        if (IsRunning) return true;
        if (!DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder))
        {
            Log?.Invoke("Le serveur n'est pas installé.");
            return false;
        }

        Directory.CreateDirectory(ProfilesFolder);
        var arguments = BuildArguments();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(ExePath, arguments)
            {
                WorkingDirectory = ServerFolder,
                UseShellExecute = false,
            },
            EnableRaisingEvents = true,
        };
        process.Exited += OnExited;

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Impossible de démarrer le serveur : {ex.Message}");
            return false;
        }

        _stopRequested = false;
        _process = process;
        StartedAt = DateTime.Now;
        Log?.Invoke("Serveur démarré.");
        Log?.Invoke($"Paramètres : {arguments}");
        StatusChanged?.Invoke();
        return true;
    }

    public async Task StopAsync()
    {
        _stopRequested = true;
        var process = _process;
        if (process == null || !IsRunning) return;

        Log?.Invoke("Arrêt du serveur…");
        try { process.CloseMainWindow(); } catch { /* pas de fenêtre */ }

        var exited = await Task.Run(() =>
        {
            try { return process.WaitForExit(15000); }
            catch { return true; }
        });

        if (!exited)
        {
            Log?.Invoke("Le serveur ne répond pas, arrêt forcé.");
            try { process.Kill(entireProcessTree: true); } catch { /* déjà arrêté */ }
        }
    }

    public async Task RestartAsync()
    {
        if (_restarting) return;
        _restarting = true;
        try
        {
            await StopAsync();
            await Task.Delay(3000);
            await StartAsync();
        }
        finally
        {
            _restarting = false;
        }
    }

    private async void OnExited(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _process)) return;

        int code;
        try { code = _process!.ExitCode; } catch { code = -1; }
        _process = null;
        StartedAt = null;
        StatusChanged?.Invoke();

        RconService.Instance.Disconnect();

        if (_stopRequested)
        {
            Log?.Invoke("Serveur arrêté.");
            if (!_restarting)
                _ = DiscordNotifier.NotifyAsync("🔴 Serveur arrêté", "Le serveur a été arrêté.", DiscordNotifier.Red);
            return;
        }

        Log?.Invoke($"Le serveur s'est arrêté de lui-même (code {code}).");
        bool autoRestart = AppSettings.Current.AutoRestart && !_restarting;
        _ = DiscordNotifier.NotifyAsync("💥 Crash du serveur",
            autoRestart ? $"Le serveur s'est arrêté de lui-même (code {code}). Redémarrage automatique dans 10 secondes."
                        : $"Le serveur s'est arrêté de lui-même (code {code}).",
            DiscordNotifier.Orange);
        if (!autoRestart) return;

        Log?.Invoke("Redémarrage automatique dans 10 secondes…");
        await Task.Delay(10000);
        if (!_stopRequested && !IsRunning) await StartAsync();
    }

    private void CheckScheduledRestart()
    {
        var s = AppSettings.Current;
        if (!s.ScheduledRestart || s.RestartHours <= 0 || !IsRunning || _restarting || StartedAt == null) return;

        var remaining = StartedAt.Value.AddHours(s.RestartHours) - DateTime.Now;

        // Avertissements en jeu à 10, 5 et 1 minute(s) du redémarrage.
        if (s.WarnBeforeRestart)
        {
            foreach (var minutes in WarningMinutes)
            {
                if (remaining > TimeSpan.FromMinutes(minutes) || remaining <= TimeSpan.Zero || _warningsSent.Contains(minutes)) continue;
                // On marque aussi les avertissements plus longs, pour ne pas les envoyer en retard.
                foreach (var longer in WarningMinutes.Where(m => m >= minutes)) _warningsSent.Add(longer);
                var label = minutes == 1 ? "1 minute" : $"{minutes} minutes";
                _ = WarnPlayersAsync($"Redémarrage du serveur dans {label} / Server restart in {minutes} min");
                break;
            }
        }

        if (remaining > TimeSpan.Zero) return;

        Log?.Invoke($"Redémarrage programmé (toutes les {s.RestartHours} h).");
        _ = DiscordNotifier.NotifyAsync("🔄 Redémarrage programmé",
            $"Redémarrage automatique (toutes les {s.RestartHours} h).", DiscordNotifier.Purple);
        _ = RestartAsync();
    }

    // ===== Mises à jour automatiques =====

    /// <summary>Met à jour le serveur et les mods juste avant le démarrage (sans rien demander à l'utilisateur).</summary>
    private async Task RunUpdatesBeforeStartAsync(AppSettings settings)
    {
        var steam = new SteamCmdService(settings.ServerFolder);
        if (!File.Exists(steam.SteamCmdExe) || string.IsNullOrWhiteSpace(settings.SteamUser))
        {
            Log?.Invoke("Mises à jour automatiques ignorées : SteamCMD ou compte Steam introuvable (fais d'abord une installation).");
            return;
        }

        // Personne n'est là pour taper un mot de passe : SteamCMD doit réutiliser la connexion enregistrée.
        steam.AskPassword = () => null;
        steam.AskSteamGuardCode = () => null;

        try
        {
            if (settings.AutoUpdateServer)
            {
                Log?.Invoke("Vérification des mises à jour du serveur DayZ…");
                var before = steam.GetInstalledBuildId();
                var result = await steam.DownloadServerAsync(settings.SteamUser, null, CancellationToken.None);
                if (!result.ServerInstallSucceeded)
                    Log?.Invoke("Mise à jour du serveur impossible (reconnecte-toi à Steam depuis l'onglet Installation). Le serveur démarre quand même.");
                else
                    Log?.Invoke(before != steam.GetInstalledBuildId() ? "Serveur DayZ mis à jour !" : "Serveur DayZ déjà à jour.");
            }

            if (settings.AutoUpdateMods)
            {
                var mods = settings.Mods.Where(m => m.Id.Length > 0).ToList();
                if (mods.Count > 0)
                {
                    Log?.Invoke($"Vérification des mises à jour de {mods.Count} mod(s)…");
                    var result = await steam.DownloadWorkshopItemsAsync(settings.SteamUser, null, mods.Select(m => m.Id), CancellationToken.None);
                    int updated = 0;
                    await Task.Run(() =>
                    {
                        foreach (var mod in mods.Where(m => result.Output.Contains($"Downloaded item {m.Id}")))
                            if (ModService.SyncMod(mod, Path.Combine(steam.WorkshopContentFolder, mod.Id), ServerFolder)) updated++;
                    });
                    settings.Save();
                    Log?.Invoke(updated > 0 ? $"{updated} mod(s) mis à jour." : "Mods déjà à jour.");
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Mises à jour automatiques impossibles : {ex.Message}");
        }
    }

    /// <summary>Toutes les 30 minutes, regarde si Bohemia a publié une nouvelle version de DayZ.</summary>
    private async Task CheckServerUpdateAsync()
    {
        var settings = AppSettings.Current;
        if (!settings.AutoUpdateServer || !IsRunning || _restarting || _updatePending || _checkingUpdate) return;
        if (DateTime.Now - _lastUpdateCheck < TimeSpan.FromMinutes(30)) return;

        _checkingUpdate = true;
        _lastUpdateCheck = DateTime.Now;
        try
        {
            var steam = new SteamCmdService(settings.ServerFolder);
            if (!File.Exists(steam.SteamCmdExe)) return;
            var latest = await steam.GetLatestBuildIdAsync(CancellationToken.None);
            var installed = steam.GetInstalledBuildId();
            if (latest == null || installed == null || latest == installed) return;
            _ = UpdateRestartAsync();
        }
        catch
        {
            // SteamCMD occupé ou Steam injoignable : on réessaiera plus tard.
        }
        finally
        {
            _checkingUpdate = false;
        }
    }

    /// <summary>Prévient les joueurs, puis redémarre (la mise à jour se fait au redémarrage).</summary>
    private async Task UpdateRestartAsync()
    {
        _updatePending = true;
        try
        {
            Log?.Invoke("Nouvelle version de DayZ disponible : redémarrage dans 5 minutes pour l'installer.");
            _ = DiscordNotifier.NotifyAsync("⬆️ Mise à jour de DayZ",
                "Une nouvelle version de DayZ est disponible : le serveur redémarre dans 5 minutes pour l'installer.", DiscordNotifier.Purple);

            if (AppSettings.Current.WarnBeforeRestart)
                await WarnPlayersAsync("Mise à jour du serveur : redémarrage dans 5 minutes / Server update: restart in 5 min");
            await Task.Delay(TimeSpan.FromMinutes(4));
            if (!IsRunning) return;

            if (AppSettings.Current.WarnBeforeRestart)
                await WarnPlayersAsync("Mise à jour du serveur : redémarrage dans 1 minute / Server update: restart in 1 min");
            await Task.Delay(TimeSpan.FromMinutes(1));
            if (IsRunning) await RestartAsync();
        }
        finally
        {
            _updatePending = false;
        }
    }

    private async Task WarnPlayersAsync(string message)
    {
        try
        {
            await RconService.Instance.SayAllAsync(message);
            Log?.Invoke($"Message envoyé aux joueurs : {message}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Avertissement impossible (RCon) : {ex.Message}");
        }
    }
}
