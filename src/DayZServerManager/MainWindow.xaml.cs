using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DayZServerManager.Services;
using DayZServerManager.Views;

namespace DayZServerManager;

public partial class MainWindow : Window
{
    private readonly InstallPage _installPage;
    private readonly UpdatePage _updatePage;
    private readonly FirewallPage _firewallPage;
    private readonly ServerPage _serverPage;
    private readonly ConfigPage _configPage;
    private readonly ModsPage _modsPage;
    private readonly BackupsPage _backupsPage;
    private readonly LogsPage _logsPage;

    public MainWindow()
    {
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "Version inconnue" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        BuildText.Text = v is { Major: 0 } ? "Version de test" : "Version officielle";


        _installPage = new InstallPage();
        _updatePage = new UpdatePage();
        _firewallPage = new FirewallPage();
        _serverPage = new ServerPage();
        _configPage = new ConfigPage();
        _modsPage = new ModsPage();
        _backupsPage = new BackupsPage();
        _logsPage = new LogsPage();
        _updatePage.UpdateAvailabilityChanged += OnUpdateAvailabilityChanged;
        ShowPage("install");

        // Vérifie les mises à jour au démarrage, sans bloquer l'application.
        Loaded += async (_, _) => await _updatePage.CheckAsync();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender is RadioButton { Tag: string tag }) ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        PageHost.Content = tag switch
        {
            "server" => _serverPage,
            "config" => _configPage,
            "mods" => _modsPage,
            "firewall" => _firewallPage,
            "backups" => _backupsPage,
            "logs" => _logsPage,
            "updates" => _updatePage,
            _ => _installPage,
        };

        if (tag == "install") _installPage.Refresh();
        if (tag == "firewall") _ = _firewallPage.RefreshAsync();
        if (tag == "server") _serverPage.Refresh();
        if (tag == "config") _configPage.Load();
        if (tag == "mods") _modsPage.Refresh();
        if (tag == "backups") _backupsPage.Refresh();
        if (tag == "logs") _logsPage.Refresh();

        // Petite animation d'apparition de la page.
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        var move = new TranslateTransform();
        PageHost.RenderTransform = move;
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
    }

    // ===== Plein écran =====
    // Une fenêtre sans bordure Windows recouvre la barre des tâches quand on l'agrandit.
    // On indique donc à Windows la zone de travail exacte de l'écran (sans la barre des tâches).

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO) FitToWorkArea(hwnd, lParam);
        return IntPtr.Zero;
    }

    private static void FitToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        const int MONITOR_DEFAULTTONEAREST = 2;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var minMax = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        minMax.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        minMax.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
        minMax.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        minMax.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
        Marshal.StructureToPtr(minMax, lParam, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void OnUpdateAvailabilityChanged(bool available)
    {
        var v = UpdateService.CurrentVersion;
        BuildText.Text = available ? "Mise à jour disponible !" : v.Major == 0 ? "Version de test" : "Version officielle";
        BuildText.Foreground = (Brush)FindResource(available ? "AccentBrush" : "CyanBrush");
    }

    private void Version_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => UpdatesNav.IsChecked = true;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
