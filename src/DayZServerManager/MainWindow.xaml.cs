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
    private readonly PlayersPage _playersPage;
    private readonly SettingsPage _settingsPage;
    private readonly EconomyPage _economyPage;
    private readonly XmlEditorPage _xmlEditorPage;
    private readonly TrayIcon _tray;
    private bool _exitRequested;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "Version inconnue" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        OnUpdateAvailabilityChanged(false);


        _installPage = new InstallPage();
        _updatePage = new UpdatePage();
        _firewallPage = new FirewallPage();
        _serverPage = new ServerPage();
        _configPage = new ConfigPage();
        _modsPage = new ModsPage();
        _backupsPage = new BackupsPage();
        _logsPage = new LogsPage();
        _playersPage = new PlayersPage();
        _settingsPage = new SettingsPage();
        _economyPage = new EconomyPage();
        _xmlEditorPage = new XmlEditorPage();
        _economyPage.EditRequested += OpenXmlEditor;

        // Le tableau de bord et les paramètres peuvent ouvrir d'autres pages.
        _serverPage.NavigateRequested += Navigate;
        _serverPage.BadgesChanged += UpdateBadges;
        _settingsPage.InstallRequested += () => Navigate("install");
        _updatePage.LatestVersionChanged += version => _serverPage.SetUpdateAvailable(version);

        // Icône près de l'horloge, et démarrage réduit quand Windows lance l'application.
        _tray = new TrayIcon(ShowFromTray, ExitApplication);
        Closing += OnClosing;
        Closed += (_, _) => _tray.Dispose();
        if (App.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
        _updatePage.UpdateAvailabilityChanged += OnUpdateAvailabilityChanged;
        SetMenuCompact(AppSettings.Current.MenuCollapsed);

        // Tant que le serveur n'est pas installé, on ouvre l'assistant d'installation.
        if (DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder))
        {
            DashboardNav.IsChecked = true;
            ShowPage("dashboard");
        }
        else
        {
            ShowPage("install");
        }

        Loaded += async (_, _) =>
        {
            if (App.StartMinimized) Hide();

            if (AppSettings.Current.AutoStartServer &&
                DependencyChecker.IsServerInstalled(AppSettings.Current.ServerFolder) &&
                !ServerManager.Instance.IsRunning)
                await ServerManager.Instance.StartAsync();

            // Vérifie les mises à jour de l'application, sans bloquer.
            await _updatePage.CheckAsync();
        };
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender is RadioButton { Tag: string tag }) ShowPage(tag);
    }

    /// <summary>Ouvre une page (depuis le tableau de bord, les paramètres…), en cochant son onglet s'il en a un.</summary>
    public void Navigate(string tag)
    {
        var item = NavPanel.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.Tag == tag);
        if (item != null)
        {
            if (item.IsChecked == true) ShowPage(tag);
            else item.IsChecked = true;
            return;
        }

        // Pages sans onglet (installation, mises à jour) : aucun onglet n'est coché.
        foreach (var other in NavPanel.Children.OfType<RadioButton>()) other.IsChecked = false;
        ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        PageHost.Content = tag switch
        {
            "dashboard" => _serverPage,
            "config" => _configPage,
            "mods" => _modsPage,
            "firewall" => _firewallPage,
            "backups" => _backupsPage,
            "players" => _playersPage,
            "settings" => _settingsPage,
            "economy" => _economyPage,
            "xml" => _xmlEditorPage,
            "logs" => _logsPage,
            "updates" => _updatePage,
            _ => _installPage,
        };

        if (tag == "install") _installPage.Refresh();
        if (tag == "firewall") _ = _firewallPage.RefreshAsync();
        if (tag == "dashboard") _serverPage.Refresh();
        if (tag == "config") _configPage.Load();
        if (tag == "mods") _modsPage.Refresh();
        if (tag == "backups") _backupsPage.Refresh();
        if (tag == "logs") _logsPage.Refresh();
        if (tag == "players") _playersPage.Refresh();
        if (tag == "settings") _settingsPage.Refresh();
        if (tag == "economy") _economyPage.Refresh();
        if (tag == "xml") _xmlEditorPage.Refresh();

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

    // ===== Zone de notification =====

    /// <summary>Affiche l'onglet Éditeur XML et y ouvre un fichier.</summary>
    public void OpenXmlEditor(string path)
    {
        Navigate("xml");
        _xmlEditorPage.OpenFile(path);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        bool reallyClosing = _exitRequested || !AppSettings.Current.CloseToTray;
        if (reallyClosing && _xmlEditorPage.HasUnsavedChanges &&
            MessageBox.Show(this, "Un fichier ouvert dans l'éditeur XML n'est pas enregistré. Quitter quand même ?",
                "Stryxhost Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            _exitRequested = false;
            ShowFromTray();
            XmlNav.IsChecked = true;
            return;
        }
        if (reallyClosing) return;
        e.Cancel = true;
        Hide();
        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray.ShowBalloon("Stryxhost Manager",
            "L'application continue de surveiller ton serveur ici. Clic droit sur l'icône pour la quitter.");
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
        // Fermeture annulée (fichier non enregistré dans l'éditeur) : on reste ouvert.
        if (_exitRequested) Application.Current.Shutdown();
    }

    // ===== Menu =====

    private bool _updateAvailable;

    private void OnUpdateAvailabilityChanged(bool available)
    {
        _updateAvailable = available;
        UpdateVersionButton();
    }

    private void UpdateVersionButton()
    {
        bool compact = AppSettings.Current.MenuCollapsed;
        var v = UpdateService.CurrentVersion;
        VersionText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BuildText.Text = compact
            ? (_updateAvailable ? "↑" : "✓")
            : _updateAvailable ? "Mise à jour disponible" : v.Major == 0 ? "Version de test" : "Version officielle";
        BuildText.Foreground = (Brush)FindResource(_updateAvailable ? "AccentBrush" : "CyanBrush");
        BuildText.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        VersionButton.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        VersionButton.Background = (Brush)FindResource(_updateAvailable ? "AccentSoftBrush" : "Panel2Brush");
        VersionButton.BorderBrush = _updateAvailable ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
        VersionButton.ToolTip = _updateAvailable ? "Une mise à jour est disponible : clique pour la voir" : $"Version {v.ToString(3)} · mises à jour";
    }

    private void Version_Click(object sender, RoutedEventArgs e) => Navigate("updates");

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.MenuCollapsed = !AppSettings.Current.MenuCollapsed;
        AppSettings.Current.Save();
        SetMenuCompact(AppSettings.Current.MenuCollapsed);
    }

    /// <summary>Menu réduit (icônes seules) ou complet.</summary>
    private void SetMenuCompact(bool compact)
    {
        MenuColumn.Width = new GridLength(compact ? 68 : 232);
        MenuBorder.Padding = compact ? new Thickness(8, 14, 8, 14) : new Thickness(10, 14, 10, 14);
        CollapseButton.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        CollapseButton.ToolTip = compact ? "Agrandir le menu" : "Réduire le menu";
        CollapseIcon.Data = Geometry.Parse(compact ? "M6 3L11 8L6 13" : "M10 3L5 8L10 13");

        foreach (var child in NavPanel.Children)
        {
            if (child is RadioButton item) Themes.NavProps.SetCompact(item, compact);
            // Les titres des groupes restent en place (invisibles) pour garder les espaces entre les groupes.
            else if (child is TextBlock header) header.Visibility = compact ? Visibility.Hidden : Visibility.Visible;
        }
        UpdateVersionButton();
    }

    /// <summary>Compteurs du menu : joueurs connectés et fichiers d'économie en erreur.</summary>
    private void UpdateBadges(int? players, int economyErrors)
    {
        Themes.NavProps.SetBadge(PlayersNav, players is > 0 ? players.Value.ToString() : "");
        Themes.NavProps.SetBadge(EconomyNav, economyErrors > 0 ? economyErrors.ToString() : "");
        Themes.NavProps.SetBadgeWarn(EconomyNav, true);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
