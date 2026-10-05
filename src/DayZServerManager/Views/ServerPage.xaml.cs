using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class ServerPage : UserControl
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _busy;
    private int _ticks;
    private int _lastPid;
    private TimeSpan _lastCpuTime;
    private DateTime _lastSample;

    public ServerPage()
    {
        InitializeComponent();

        var settings = AppSettings.Current;
        AutoRestartToggle.IsChecked = settings.AutoRestart;
        ScheduledToggle.IsChecked = settings.ScheduledRestart;
        HoursBox.Text = settings.RestartHours.ToString();

        var manager = ServerManager.Instance;
        manager.Log += message => Dispatcher.InvokeAsync(() => Log(message));
        manager.StatusChanged += () => Dispatcher.InvokeAsync(UpdateStatus);

        _clock.Tick += (_, _) =>
        {
            UpdateUptime();
            if (++_ticks % 2 == 0) UpdateResources();
        };
        _clock.Start();

        manager.AttachToRunning();
        UpdateStatus();
        UpdateResources();
    }

    /// <summary>Remet à jour l'affichage (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh() => UpdateStatus();

    // ===== Actions =====

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder))
        {
            Log("Installe d'abord le serveur depuis l'onglet Installation.");
            return;
        }
        _busy = true;
        UpdateStatus();
        try { await ServerManager.Instance.StartAsync(); }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        UpdateStatus();
        try { await ServerManager.Instance.StopAsync(); }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        UpdateStatus();
        try { await ServerManager.Instance.RestartAsync(); }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private void Options_Changed(object sender, RoutedEventArgs e) => SaveOptions();

    private void Hours_LostFocus(object sender, RoutedEventArgs e) => SaveOptions();

    private void SaveOptions()
    {
        var settings = AppSettings.Current;
        settings.AutoRestart = AutoRestartToggle.IsChecked == true;
        settings.ScheduledRestart = ScheduledToggle.IsChecked == true;
        if (int.TryParse(HoursBox.Text.Trim(), out var hours) && hours >= 1 && hours <= 48)
            settings.RestartHours = hours;
        else
            HoursBox.Text = settings.RestartHours.ToString();
        settings.Save();
    }

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

    // ===== Affichage =====

    private void UpdateStatus()
    {
        var manager = ServerManager.Instance;
        bool installed = DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder);
        bool running = manager.IsRunning;

        StatusText.Text = !installed ? "NON INSTALLÉ" : running ? "EN LIGNE" : "HORS LIGNE";
        StatusText.Foreground = Res(running ? "CyanBrush" : installed ? "TextBrush" : "WarnBrush");
        StatusDot.Fill = Res(running ? "CyanBrush" : installed ? "LineBrush" : "WarnBrush");

        var settings = AppSettings.Current;
        int activeMods = settings.Mods.Count(m => m.Enabled);
        InfoText.Text = $"Port de jeu : {settings.GamePort} · Mods actifs : {activeMods}";

        StartButton.IsEnabled = installed && !running && !_busy;
        StopButton.IsEnabled = running && !_busy;
        RestartButton.IsEnabled = running && !_busy;
        UpdateUptime();
    }

    private void UpdateUptime()
    {
        var started = ServerManager.Instance.StartedAt;
        if (started == null || !ServerManager.Instance.IsRunning)
        {
            UptimeText.Text = "Le serveur n'est pas lancé.";
            return;
        }
        var up = DateTime.Now - started.Value;
        UptimeText.Text = $"En ligne depuis {(int)up.TotalHours} h {up.Minutes:00} min {up.Seconds:00} s";
    }

    // ===== Ressources (uniquement ce que le serveur utilise) =====

    private long _serverFolderBytes = -1;
    private DateTime _folderMeasuredAt;
    private bool _measuringFolder;

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

        // Processeur utilisé par le serveur.
        CpuValue.Text = !running ? "—" : cpuReady ? $"{cpuPercent:0} %" : "…";
        CpuBar.Value = running ? cpuPercent : 0;
        CpuInfo.Text = running
            ? $"Sur les {Environment.ProcessorCount} cœurs du processeur"
            : "Serveur hors ligne";

        // Mémoire utilisée par le serveur, comparée à la RAM du PC.
        var memory = SystemMetrics.GetMemory();
        ulong totalRam = memory?.Total ?? 0;
        RamValue.Text = running ? FormatBytes(ramBytes) : "—";
        RamBar.Value = running && totalRam > 0 ? ramBytes * 100.0 / totalRam : 0;
        RamInfo.Text = !running
            ? "Serveur hors ligne"
            : totalRam > 0
                ? $"{ramBytes * 100.0 / totalRam:0} % des {FormatBytes((long)totalRam)} de RAM du PC"
                : "";

        UpdateDisk();
    }

    /// <summary>Place occupée par le dossier du serveur (mesurée en arrière-plan toutes les 5 minutes).</summary>
    private void UpdateDisk()
    {
        var mainFolder = AppSettings.Current.ServerFolder;
        if (!DependencyChecker.IsServerInstalled(mainFolder))
        {
            DiskValue.Text = "—";
            DiskBar.Value = 0;
            DiskInfo.Text = "Serveur non installé";
            return;
        }

        DriveInfo? drive = null;
        try { drive = new DriveInfo(Path.GetPathRoot(mainFolder)!); } catch { /* lecteur illisible */ }

        if (_serverFolderBytes >= 0)
        {
            DiskValue.Text = FormatBytes(_serverFolderBytes);
            DiskBar.Value = drive != null && drive.TotalSize > 0 ? _serverFolderBytes * 100.0 / drive.TotalSize : 0;
            DiskInfo.Text = drive != null
                ? $"Dossier du serveur · {FormatBytes(drive.AvailableFreeSpace)} libres sur {drive.Name}"
                : "Dossier du serveur";
        }
        else
        {
            DiskValue.Text = "…";
            DiskInfo.Text = "Calcul de la taille du serveur…";
        }

        if (_measuringFolder || (_serverFolderBytes >= 0 && DateTime.UtcNow - _folderMeasuredAt < TimeSpan.FromMinutes(5)))
            return;

        _measuringFolder = true;
        var serverFolder = ServerManager.ServerFolder;
        Task.Run(() => FolderSize(serverFolder)).ContinueWith(task =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (task.Status == TaskStatus.RanToCompletion) _serverFolderBytes = task.Result;
                _folderMeasuredAt = DateTime.UtcNow;
                _measuringFolder = false;
                UpdateDisk();
            });
        });
    }

    private static long FolderSize(string folder)
    {
        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
        {
            try { total += file.Length; } catch { /* fichier en cours d'utilisation */ }
        }
        return total;
    }

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
