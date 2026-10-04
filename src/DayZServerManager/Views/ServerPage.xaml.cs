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

        _clock.Tick += (_, _) => UpdateUptime();
        _clock.Start();

        manager.AttachToRunning();
        UpdateStatus();
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

    private void Log(string message)
    {
        LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        LogScroll.ScrollToEnd();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
