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

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder))
        {
            Log("Installe d'abord le serveur depuis l'onglet Installation.");
            return;
        }
        ServerManager.Instance.Start();
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

    // ===== Ressources =====

    private void UpdateResources()
    {
        // Processeur : celui du serveur en grand, celui de tout le PC dans la barre.
        var pcCpu = SystemMetrics.GetCpuUsage();
        CpuBar.Value = pcCpu ?? 0;
        CpuInfo.Text = pcCpu.HasValue ? $"Total du PC : {pcCpu:0} %" : "Total du PC : …";

        string serverCpu = "—", serverRam = "—";
        var process = ServerManager.Instance.ServerProcess;
        if (process != null && ServerManager.Instance.IsRunning)
        {
            try
            {
                process.Refresh();
                var now = DateTime.UtcNow;
                var cpuTime = process.TotalProcessorTime;
                if (_lastPid == process.Id && _lastSample != default)
                {
                    var elapsed = (now - _lastSample).TotalMilliseconds * Environment.ProcessorCount;
                    var percent = elapsed > 0 ? (cpuTime - _lastCpuTime).TotalMilliseconds / elapsed * 100 : 0;
                    serverCpu = $"{Math.Clamp(percent, 0, 100):0} %";
                }
                else
                {
                    serverCpu = "…";
                }
                _lastPid = process.Id;
                _lastCpuTime = cpuTime;
                _lastSample = now;
                serverRam = FormatBytes(process.WorkingSet64);
            }
            catch
            {
                // Le serveur vient de s'arrêter.
            }
        }
        else
        {
            _lastSample = default;
        }
        CpuValue.Text = serverCpu;
        RamValue.Text = serverRam;

        // Mémoire de tout le PC.
        var memory = SystemMetrics.GetMemory();
        if (memory is { Total: > 0 } mem)
        {
            var used = mem.Total - mem.Available;
            var percent = used * 100.0 / mem.Total;
            RamBar.Value = percent;
            RamInfo.Text = $"PC : {FormatBytes((long)used)} sur {FormatBytes((long)mem.Total)} ({percent:0} %)";
        }

        // Disque où est installé le serveur.
        try
        {
            var folder = AppSettings.Current.ServerFolder;
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException();
            var drive = new DriveInfo(Path.GetPathRoot(folder)!);
            var usedPercent = (drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize;
            DiskValue.Text = FormatBytes(drive.AvailableFreeSpace);
            DiskBar.Value = usedPercent;
            DiskInfo.Text = $"{drive.Name} · {usedPercent:0} % utilisé sur {FormatBytes(drive.TotalSize)}";
        }
        catch
        {
            DiskValue.Text = "—";
            DiskBar.Value = 0;
            DiskInfo.Text = "Aucun dossier d'installation choisi.";
        }
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
