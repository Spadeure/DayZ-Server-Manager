using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DayZServerManager.Views;

namespace DayZServerManager;

public partial class MainWindow : Window
{
    private readonly InstallPage _installPage;

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
        ShowPage("install");
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
            "server" => new PlaceholderPage("SERVEUR", "Démarrer, arrêter et surveiller ton serveur DayZ."),
            "config" => new PlaceholderPage("CONFIGURATION", "Nom du serveur, mot de passe, joueurs, carte…"),
            "mods" => new PlaceholderPage("MODS", "Installer et mettre à jour les mods du Workshop."),
            "firewall" => new PlaceholderPage("PARE-FEU", "Ouvrir les ports du serveur dans le pare-feu Windows."),
            "logs" => new PlaceholderPage("JOURNAUX", "Consulter les journaux du serveur et de l'application."),
            _ => _installPage,
        };

        // Petite animation d'apparition de la page.
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        var move = new TranslateTransform();
        PageHost.RenderTransform = move;
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
