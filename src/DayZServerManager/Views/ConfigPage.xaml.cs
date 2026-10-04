using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class ConfigPage : UserControl
{
    private static readonly (string Name, string Template)[] Maps =
    [
        ("Chernarus", "dayzOffline.chernarusplus"),
        ("Livonia", "dayzOffline.enoch"),
        ("Sakhal", "dayzOffline.sakhal"),
    ];

    private ServerConfigFile? _config;

    public ConfigPage()
    {
        InitializeComponent();
    }

    private static string ConfigPath => Path.Combine(ServerManager.ServerFolder, "serverDZ.cfg");
    private static string BattlEyeConfigPath => Path.Combine(ServerManager.ServerFolder, "battleye", "BEServer_x64.cfg");

    private RadioButton[] MapChips => [ChernarusChip, LivoniaChip, SakhalChip];

    /// <summary>Relit serverDZ.cfg (appelé à chaque ouverture de l'onglet).</summary>
    public void Load()
    {
        StatusText.Text = "";
        if (!File.Exists(ConfigPath))
        {
            _config = null;
            NotInstalledPanel.Visibility = Visibility.Visible;
            EditorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NotInstalledPanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;

        try
        {
            _config = ServerConfigFile.Load(ConfigPath);
        }
        catch (Exception ex)
        {
            ShowStatus($"Impossible de lire serverDZ.cfg : {ex.Message}", "WarnBrush");
            return;
        }

        HostnameBox.Text = _config.Get("hostname") ?? "";
        JoinPasswordBox.Text = _config.Get("password") ?? "";
        AdminPasswordBox.Text = _config.Get("passwordAdmin") ?? "";
        MaxPlayersBox.Text = _config.Get("maxPlayers") ?? "60";
        ThirdPersonToggle.IsChecked = _config.Get("disable3rdPerson") != "1";
        CrosshairToggle.IsChecked = _config.Get("disableCrosshair") != "1";
        VoiceToggle.IsChecked = _config.Get("disableVoN") != "1";
        PersistentTimeToggle.IsChecked = _config.Get("serverTimePersistent") == "1";
        DayAccelBox.Text = _config.Get("serverTimeAcceleration") ?? "1";
        NightAccelBox.Text = _config.Get("serverNightTimeAcceleration") ?? "1";
        RconPasswordBox.Text = ReadRconPassword();

        // Carte : on ne propose que celles présentes dans le dossier mpmissions.
        var template = _config.Get("template") ?? Maps[0].Template;
        var available = new List<string>();
        for (int i = 0; i < Maps.Length; i++)
        {
            bool exists = Directory.Exists(Path.Combine(ServerManager.ServerFolder, "mpmissions", Maps[i].Template));
            MapChips[i].IsEnabled = exists;
            MapChips[i].IsChecked = string.Equals(template, Maps[i].Template, StringComparison.OrdinalIgnoreCase);
            if (exists) available.Add(Maps[i].Name);
        }
        MapInfo.Text = MapChips.Any(c => c.IsChecked == true)
            ? $"Cartes disponibles sur ce serveur : {string.Join(", ", available)}."
            : $"Carte personnalisée : {template} (choisis une carte ci-dessus pour la remplacer).";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        if (HostnameBox.Text.Trim().Length == 0)
        {
            ShowStatus("Donne un nom au serveur.", "WarnBrush");
            return;
        }
        if (!int.TryParse(MaxPlayersBox.Text.Trim(), out var maxPlayers) || maxPlayers < 1 || maxPlayers > 127)
        {
            ShowStatus("Le nombre de joueurs doit être entre 1 et 127.", "WarnBrush");
            return;
        }
        if (!TryReadNumber(DayAccelBox.Text, 0.1, 24, out var dayAccel))
        {
            ShowStatus("La vitesse du temps le jour doit être entre 0,1 et 24.", "WarnBrush");
            return;
        }
        if (!TryReadNumber(NightAccelBox.Text, 0.1, 64, out var nightAccel))
        {
            ShowStatus("La vitesse du temps la nuit doit être entre 0,1 et 64.", "WarnBrush");
            return;
        }

        try
        {
            _config.SetString("hostname", HostnameBox.Text.Trim());
            _config.SetString("password", JoinPasswordBox.Text.Trim());
            _config.SetString("passwordAdmin", AdminPasswordBox.Text.Trim());
            _config.SetNumber("maxPlayers", maxPlayers);
            _config.SetNumber("disable3rdPerson", ThirdPersonToggle.IsChecked == true ? 0 : 1);
            _config.SetNumber("disableCrosshair", CrosshairToggle.IsChecked == true ? 0 : 1);
            _config.SetNumber("disableVoN", VoiceToggle.IsChecked == true ? 0 : 1);
            _config.SetNumber("serverTimePersistent", PersistentTimeToggle.IsChecked == true ? 1 : 0);
            _config.SetNumber("serverTimeAcceleration", dayAccel);
            _config.SetNumber("serverNightTimeAcceleration", nightAccel);
            _config.SetNumber("steamQueryPort", AppSettings.Current.QueryPort);

            for (int i = 0; i < Maps.Length; i++)
                if (MapChips[i].IsChecked == true) _config.SetString("template", Maps[i].Template);

            _config.Save();
            WriteBattlEyeConfig(RconPasswordBox.Text.Trim());

            ShowStatus(ServerManager.Instance.IsRunning
                ? "Enregistré ! Redémarre le serveur pour appliquer les changements."
                : "Enregistré !", "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Impossible d'enregistrer : {ex.Message}", "WarnBrush");
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(ConfigPath)) return;
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{ConfigPath}\"") { UseShellExecute = true });
    }

    // ===== BattlEye (RCon) =====

    private static string ReadRconPassword()
    {
        try
        {
            if (!File.Exists(BattlEyeConfigPath)) return "";
            foreach (var line in File.ReadAllLines(BattlEyeConfigPath))
                if (line.TrimStart().StartsWith("RConPassword", StringComparison.OrdinalIgnoreCase))
                    return line.Trim()["RConPassword".Length..].Trim();
        }
        catch
        {
            // Fichier illisible : champ vide.
        }
        return "";
    }

    private static void WriteBattlEyeConfig(string password)
    {
        if (password.Length == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(BattlEyeConfigPath)!);
        File.WriteAllLines(BattlEyeConfigPath, new[]
        {
            $"RConPassword {password}",
            $"RConPort {AppSettings.Current.RconPort}",
            "RestrictRCon 0",
        });
    }

    // ===== Outils =====

    private static bool TryReadNumber(string text, double min, double max, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && value >= min && value <= max;

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)Application.Current.FindResource(brushKey);
    }
}
