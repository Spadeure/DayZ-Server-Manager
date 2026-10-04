using System.Reflection;
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

    public MainWindow()
    {
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "Version inconnue" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        BuildText.Text = v is { Major: 0 } ? "Version de test" : "Version officielle";

        // Fenêtre sans bordure Windows : on corrige le débordement quand elle est agrandie.
        StateChanged += (_, _) =>
            RootBorder.Padding = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

        _installPage = new InstallPage();
        _updatePage = new UpdatePage();
        _firewallPage = new FirewallPage();
        _serverPage = new ServerPage();
        _configPage = new ConfigPage();
        _modsPage = new ModsPage();
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
            "logs" => new PlaceholderPage("JOURNAUX", "Consulter les journaux du serveur et de l'application."),
            "updates" => _updatePage,
            _ => _installPage,
        };

        if (tag == "install") _installPage.Refresh();
        if (tag == "firewall") _ = _firewallPage.RefreshAsync();
        if (tag == "server") _serverPage.Refresh();
        if (tag == "config") _configPage.Load();
        if (tag == "mods") _modsPage.Refresh();

        // Petite animation d'apparition de la page.
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        var move = new TranslateTransform();
        PageHost.RenderTransform = move;
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
    }

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
