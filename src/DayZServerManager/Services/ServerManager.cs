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
    private readonly System.Threading.Timer _scheduleTimer;

    private ServerManager()
    {
        _scheduleTimer = new System.Threading.Timer(_ => CheckScheduledRestart(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
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

    public static string BuildArguments()
    {
        var s = AppSettings.Current;
        var args = new List<string>
        {
            "-config=serverDZ.cfg", $"-port={s.GamePort}", "-profiles=profiles", "-BEpath=battleye",
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

    public bool Start()
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
            Start();
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

        if (_stopRequested)
        {
            Log?.Invoke("Serveur arrêté.");
            return;
        }

        Log?.Invoke($"Le serveur s'est arrêté de lui-même (code {code}).");
        if (!AppSettings.Current.AutoRestart || _restarting) return;

        Log?.Invoke("Redémarrage automatique dans 10 secondes…");
        await Task.Delay(10000);
        if (!_stopRequested && !IsRunning) Start();
    }

    private void CheckScheduledRestart()
    {
        var s = AppSettings.Current;
        if (!s.ScheduledRestart || s.RestartHours <= 0 || !IsRunning || _restarting || StartedAt == null) return;
        if (DateTime.Now - StartedAt.Value < TimeSpan.FromHours(s.RestartHours)) return;

        Log?.Invoke($"Redémarrage programmé (toutes les {s.RestartHours} h).");
        _ = RestartAsync();
    }
}
