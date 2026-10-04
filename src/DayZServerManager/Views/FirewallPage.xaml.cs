using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class FirewallPage : UserControl
{
    private bool _busy;

    public FirewallPage()
    {
        InitializeComponent();
        var settings = AppSettings.Current;
        GamePortBox.Text = settings.GamePort.ToString();
        QueryPortBox.Text = settings.QueryPort.ToString();
        RconPortBox.Text = settings.RconPort.ToString();
        UpdateDescriptions();
    }

    // ===== État des règles =====

    public async Task RefreshAsync()
    {
        if (_busy) return;
        UpdateDescriptions();
        foreach (var (pill, text) in Pills()) SetPill(pill, text, null);

        try
        {
            var status = await FirewallService.GetStatusAsync();
            SetPill(GamePill, GameStatus, status[FirewallService.GameRule]);
            SetPill(QueryPill, QueryStatus, status[FirewallService.QueryRule]);
            SetPill(RconPill, RconStatus, status[FirewallService.RconRule]);
            SetPill(ProgramPill, ProgramStatus, status[FirewallService.ProgramRule]);
        }
        catch (Exception ex)
        {
            ShowStatus($"Impossible de lire le pare-feu : {ex.Message}", "WarnBrush");
        }
    }

    // ===== Actions =====

    private async void Recheck_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        ShowStatus("État du pare-feu revérifié.", "MutedBrush");
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !TryReadPorts(out var game, out var query, out var rcon)) return;

        var settings = AppSettings.Current;
        settings.GamePort = game;
        settings.QueryPort = query;
        settings.RconPort = rcon;
        settings.Save();

        SetBusy(true);
        ShowStatus("Ouverture des ports en cours…", "MutedBrush");
        var problems = new List<string>();

        try
        {
            await Apply(FirewallService.OpenPortAsync(FirewallService.GameRule, "UDP", $"{game}-{game + 3}"), "port de jeu", problems);
            await Apply(FirewallService.OpenPortAsync(FirewallService.QueryRule, "UDP", query.ToString()), "port de requête Steam", problems);
            await Apply(FirewallService.OpenPortAsync(FirewallService.RconRule, "UDP", rcon.ToString()), "port RCon", problems);

            var serverExe = ServerExePath();
            if (serverExe != null)
                await Apply(FirewallService.AllowProgramAsync(FirewallService.ProgramRule, serverExe), "programme du serveur", problems);

            if (problems.Count > 0)
                ShowStatus($"Problème avec : {string.Join(", ", problems)}.", "WarnBrush");
            else if (serverExe == null)
                ShowStatus("Ports ouverts. Le programme du serveur sera autorisé une fois le serveur installé.", "CyanBrush");
            else
                ShowStatus("Tout est ouvert dans le pare-feu Windows !", "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        ShowStatus("Fermeture des ports en cours…", "MutedBrush");
        try
        {
            foreach (var rule in FirewallService.AllRules)
                await FirewallService.DeleteRuleAsync(rule);
            ShowStatus("Les règles du serveur ont été retirées du pare-feu.", "MutedBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Erreur : {ex.Message}", "WarnBrush");
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private static async Task Apply(Task<NetshResult> action, string label, List<string> problems)
    {
        var result = await action;
        if (!result.Success) problems.Add(label);
    }

    // ===== Saisie des ports =====

    private void Port_TextChanged(object sender, TextChangedEventArgs e) => UpdateDescriptions();

    private bool TryReadPorts(out int game, out int query, out int rcon)
    {
        query = rcon = 0;
        if (!ReadPort(GamePortBox, "port de jeu", out game) ||
            !ReadPort(QueryPortBox, "port de requête Steam", out query) ||
            !ReadPort(RconPortBox, "port RCon", out rcon))
            return false;

        if (game + 3 > 65535)
        {
            ShowStatus("Le port de jeu est trop grand (il faut de la place pour les 3 ports suivants).", "WarnBrush");
            return false;
        }

        int first = game; bool InGameRange(int port) => port >= first && port <= first + 3;
        if (InGameRange(query) || InGameRange(rcon) || query == rcon)
        {
            ShowStatus($"Les ports doivent être différents, et ne pas être entre {game} et {game + 3}.", "WarnBrush");
            return false;
        }
        return true;
    }

    private bool ReadPort(TextBox box, string label, out int port)
    {
        if (int.TryParse(box.Text.Trim(), out port) && port >= 1024 && port <= 65535) return true;
        ShowStatus($"Le {label} doit être un nombre entre 1024 et 65535.", "WarnBrush");
        box.Focus();
        return false;
    }

    // ===== Affichage =====

    private void UpdateDescriptions()
    {
        if (GameDesc == null) return;
        GameDesc.Text = int.TryParse(GamePortBox.Text.Trim(), out var game)
            ? $"UDP · ports {game} à {game + 3}, utilisés par les joueurs"
            : "UDP · utilisé par les joueurs";
        QueryDesc.Text = "UDP · affiche le serveur dans la liste du jeu";
        RconDesc.Text = "UDP · administration à distance (BattlEye)";
        var exe = ServerExePath();
        ProgramDesc.Text = exe != null
            ? "Autorise DayZServer_x64.exe dans le pare-feu"
            : "Disponible une fois le serveur installé";
    }

    private static string? ServerExePath()
    {
        var folder = AppSettings.Current.ServerFolder;
        return DependencyChecker.IsServerInstalled(folder)
            ? Path.Combine(DependencyChecker.ServerFolder(folder), "DayZServer_x64.exe")
            : null;
    }

    private IEnumerable<(Border, TextBlock)> Pills() =>
    [
        (GamePill, GameStatus), (QueryPill, QueryStatus), (RconPill, RconStatus), (ProgramPill, ProgramStatus),
    ];

    private static void SetPill(Border pill, TextBlock text, bool? open)
    {
        text.Text = open switch { true => "Ouvert", false => "Fermé", null => "…" };
        pill.Background = Res(open == true ? "CyanSoftBrush" : open == false ? "WarnSoftBrush" : "Panel2Brush");
        text.Foreground = Res(open == true ? "CyanBrush" : open == false ? "WarnBrush" : "MutedBrush");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        OpenButton.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
        RecheckButton.IsEnabled = !busy;
    }

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = Res(brushKey);
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
