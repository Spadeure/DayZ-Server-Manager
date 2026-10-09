using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace DayZServerManager.Services;

public record SteamCmdResult(int ExitCode, string Output)
{
    public bool ServerInstallSucceeded => Output.Contains("Success! App '", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Pilote SteamCMD : installation, connexion à Steam et téléchargement du serveur DayZ.</summary>
public sealed class SteamCmdService
{
    public const string DayZServerAppId = "223350";
    public const string DayZExperimentalAppId = "1042420";
    public const string DayZWorkshopAppId = "221100";

    /// <summary>Serveur stable ou expérimental, selon les réglages.</summary>
    public static string ServerAppId =>
        AppSettings.Current.ServerBranch == "experimental" ? DayZExperimentalAppId : DayZServerAppId;

    // Un seul SteamCMD à la fois (installation du serveur ou mods).
    private static readonly SemaphoreSlim SteamLock = new(1, 1);
    private const string DownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

    private static readonly HttpClient Http = new();
    private static readonly Regex ProgressRegex = new(
        @"Update state \(0x[0-9a-fA-F]+\)\s*([a-z ]+?),\s*progress:\s*([\d.]+)\s*\((\d+)\s*/\s*(\d+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _mainFolder;
    private string _lastStage = "";

    public SteamCmdService(string mainFolder) => _mainFolder = mainFolder;

    public string SteamCmdFolder => DependencyChecker.SteamCmdFolder(_mainFolder);
    public string SteamCmdExe => Path.Combine(SteamCmdFolder, "steamcmd.exe");
    public string ServerFolder => DependencyChecker.ServerFolder(_mainFolder);
    public string WorkshopContentFolder =>
        Path.Combine(SteamCmdFolder, "steamapps", "workshop", "content", DayZWorkshopAppId);

    /// <summary>Une ligne à afficher dans le journal (déjà traduite quand c'est possible).</summary>
    public event Action<string>? Output;

    /// <summary>Progression : étape, pourcentage, octets faits, octets au total.</summary>
    public event Action<string, double, long, long>? Progress;

    /// <summary>Appelé quand SteamCMD demande un code Steam Guard. Renvoie null pour abandonner.</summary>
    public Func<string?>? AskSteamGuardCode { get; set; }

    /// <summary>Appelé quand SteamCMD demande le mot de passe. Renvoie null pour abandonner.</summary>
    public Func<string?>? AskPassword { get; set; }

    // ===== Installation de SteamCMD =====

    public async Task InstallSteamCmdAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(SteamCmdFolder);
        var zipPath = Path.Combine(SteamCmdFolder, "steamcmd.zip");

        Output?.Invoke("Téléchargement de SteamCMD…");
        using (var response = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(zipPath);
            await response.Content.CopyToAsync(file, ct);
        }

        ZipFile.ExtractToDirectory(zipPath, SteamCmdFolder, overwriteFiles: true);
        File.Delete(zipPath);

        Output?.Invoke("SteamCMD installé. Première mise à jour de SteamCMD (une à deux minutes)…");
        await RunAsync("+quit", ct);
        Output?.Invoke("SteamCMD est prêt.");
    }

    // ===== Téléchargement du serveur =====

    /// <summary>Installe ou met à jour le serveur. Sans mot de passe, SteamCMD réutilise la connexion enregistrée.</summary>
    public async Task<SteamCmdResult> DownloadServerAsync(string user, string? password, CancellationToken ct)
    {
        Directory.CreateDirectory(ServerFolder);
        _lastStage = "";

        // Une mise à jour remet serverDZ.cfg comme à l'origine : on garde une copie pour la remettre après.
        var configPath = Path.Combine(ServerFolder, "serverDZ.cfg");
        string? savedConfig = null;
        try { if (File.Exists(configPath)) savedConfig = File.ReadAllText(configPath); }
        catch { /* fichier illisible */ }

        // « validate » (vérification complète) seulement à la première installation :
        // ensuite, il écraserait les fichiers que tu as modifiés.
        bool firstInstall = !File.Exists(Path.Combine(ServerFolder, "DayZServer_x64.exe"));
        var login = string.IsNullOrEmpty(password) ? Quote(user) : $"{Quote(user)} {Quote(password)}";
        var args = $"+force_install_dir {Quote(ServerFolder)} +login {login} +app_update {ServerAppId}" +
                   (firstInstall ? " validate" : "") + " +quit";

        try
        {
            return await RunAsync(args, ct);
        }
        finally
        {
            if (savedConfig != null)
            {
                try
                {
                    File.WriteAllText(configPath, savedConfig);
                    Output?.Invoke("Ta configuration (serverDZ.cfg) a été conservée.");
                }
                catch
                {
                    Output?.Invoke("Attention : impossible de remettre ta configuration serverDZ.cfg, vérifie l'onglet Configuration.");
                }
            }
        }
    }

    /// <summary>Numéro de la dernière version publiée par Bohemia (null si inconnu).</summary>
    public async Task<string?> GetLatestBuildIdAsync(CancellationToken ct)
    {
        var result = await RunAsync($"+login anonymous +app_info_update 1 +app_info_print {ServerAppId} +quit", ct);
        var match = Regex.Match(result.Output, "\"public\"\\s*\\{?\\s*\"buildid\"\\s*\"(\\d+)\"", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Numéro de la version installée (null si inconnu).</summary>
    public string? GetInstalledBuildId()
    {
        try
        {
            var manifest = Path.Combine(ServerFolder, "steamapps", $"appmanifest_{ServerAppId}.acf");
            if (!File.Exists(manifest)) return null;
            var match = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s*\"(\\d+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    // ===== Téléchargement des mods du Workshop =====

    /// <summary>Télécharge des mods. Sans mot de passe, SteamCMD réutilise la connexion enregistrée.</summary>
    public Task<SteamCmdResult> DownloadWorkshopItemsAsync(string user, string? password, IEnumerable<string> ids,
        CancellationToken ct)
    {
        var args = new StringBuilder($"+login {Quote(user)}");
        if (!string.IsNullOrEmpty(password)) args.Append(' ').Append(Quote(password));
        foreach (var id in ids) args.Append($" +workshop_download_item {DayZWorkshopAppId} {id} validate");
        args.Append(" +quit");
        return RunAsync(args.ToString(), ct);
    }

    // ===== Lancement de SteamCMD =====

    private static readonly Regex CursorMoveRegex = new(@"\x1B\[\d*(;\d*)?H", RegexOptions.Compiled);
    private static readonly Regex EscapeRegex = new(
        @"\x1B\][^\x07\x1B]*(\x07|\x1B\\)|\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B[@-Z\\-_]", RegexOptions.Compiled);

    private readonly HashSet<string> _seenLines = new();

    private async Task<SteamCmdResult> RunAsync(string arguments, CancellationToken ct)
    {
        if (!await SteamLock.WaitAsync(0, ct))
            throw new InvalidOperationException("SteamCMD est déjà en cours d'utilisation (installation ou mods). Attends la fin.");

        try
        {
            var all = new StringBuilder();
            _seenLines.Clear();

            using var console = PseudoConsoleProcess.Start(SteamCmdExe, arguments, SteamCmdFolder);
            using var registration = ct.Register(console.Kill);

            var readTask = Task.Run(() => ReadOutputAsync(console, all));
            await console.WaitForAllExitAsync();
            await Task.Run(console.CloseConsole);
            await readTask;

            ct.ThrowIfCancellationRequested();
            string text;
            lock (all) text = all.ToString();
            return new SteamCmdResult(console.ExitCode, text);
        }
        finally
        {
            SteamLock.Release();
        }
    }

    private async Task ReadOutputAsync(PseudoConsoleProcess console, StringBuilder all)
    {
        using var reader = new StreamReader(console.Output, new UTF8Encoding(false));
        var buffer = new char[4096];
        var pending = new StringBuilder();

        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                pending.Append(buffer, 0, read);
                var raw = pending.ToString();

                int cut = raw.LastIndexOfAny(new[] { '\n', '\r' });
                if (cut >= 0)
                {
                    foreach (var line in Clean(raw[..cut]).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        HandleLine(line, all);
                    raw = raw[(cut + 1)..];
                    pending.Clear().Append(raw);
                }

                // Les questions de SteamCMD ne finissent pas par un retour à la ligne.
                if (IsPasswordPrompt(Clean(raw)))
                {
                    pending.Clear();
                    Output?.Invoke("Steam demande ton mot de passe.");
                    var password = AskPassword?.Invoke();
                    if (string.IsNullOrEmpty(password))
                    {
                        Output?.Invoke("Aucun mot de passe saisi : connexion abandonnée.");
                        console.Kill();
                        return;
                    }
                    console.Write(password + "\r");
                }
                else if (IsCodePrompt(Clean(raw)))
                {
                    pending.Clear();
                    Output?.Invoke("Steam demande un code Steam Guard.");
                    var code = AskSteamGuardCode?.Invoke();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        Output?.Invoke("Aucun code saisi : connexion abandonnée.");
                        console.Kill();
                        return;
                    }
                    console.Write(code.Trim() + "\r");
                }
            }
        }
        catch (IOException)
        {
            // La console a été fermée : fin normale de la lecture.
        }
        catch (ObjectDisposedException)
        {
            // Idem.
        }

        if (pending.Length > 0)
            foreach (var line in Clean(pending.ToString()).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                HandleLine(line, all);
    }

    /// <summary>Retire les codes de mise en forme de la console (couleurs, curseur…).</summary>
    private static string Clean(string text) =>
        EscapeRegex.Replace(CursorMoveRegex.Replace(text, "\n"), "").Replace("\b", "");

    private static bool IsPasswordPrompt(string text) =>
        text.TrimEnd().EndsWith("password:", StringComparison.OrdinalIgnoreCase);

    private static bool IsCodePrompt(string text)
    {
        var t = text.TrimEnd();
        return t.EndsWith("code:", StringComparison.OrdinalIgnoreCase);
    }

    private void HandleLine(string raw, StringBuilder all)
    {
        var line = raw.Trim();
        if (line.Length == 0) return;
        lock (all) all.AppendLine(line);
        // La console peut réafficher d'anciennes lignes : on ne les répète pas à l'écran.
        if (!_seenLines.Add(line)) return;

        var match = ProgressRegex.Match(line);
        if (match.Success)
        {
            var stage = TranslateStage(match.Groups[1].Value);
            double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent);
            long.TryParse(match.Groups[3].Value, out var done);
            long.TryParse(match.Groups[4].Value, out var total);

            if (stage != _lastStage)
            {
                _lastStage = stage;
                Output?.Invoke($"{stage}…");
            }
            Progress?.Invoke(stage, percent, done, total);
            return;
        }

        Output?.Invoke(Translate(line));
    }

    private static string TranslateStage(string stage)
    {
        stage = stage.ToLowerInvariant();
        if (stage.Contains("download")) return "Téléchargement";
        if (stage.Contains("verif") || stage.Contains("validat")) return "Vérification des fichiers";
        if (stage.Contains("prealloc")) return "Préparation de l'espace disque";
        if (stage.Contains("commit")) return "Finalisation";
        return "Mise à jour";
    }

    private static string Translate(string line)
    {
        if (line.Contains("Steam Mobile app", StringComparison.OrdinalIgnoreCase))
            return "→ Confirme la connexion dans l'application Steam de ton téléphone…";
        if (line.Contains("Invalid Password", StringComparison.OrdinalIgnoreCase))
            return "Nom d'utilisateur ou mot de passe incorrect.";
        if (line.Contains("Rate Limit Exceeded", StringComparison.OrdinalIgnoreCase))
            return "Trop de tentatives de connexion : attends quelques minutes avant de réessayer.";
        if (line.Contains("No subscription", StringComparison.OrdinalIgnoreCase))
            return "Ce compte Steam ne possède pas DayZ : il faut un compte qui possède le jeu.";
        if (line.Contains("Invalid Login Auth Code", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Two-factor code mismatch", StringComparison.OrdinalIgnoreCase))
            return "Code Steam Guard incorrect.";
        if (line.Contains("Logging in user", StringComparison.OrdinalIgnoreCase))
            return "Connexion au compte Steam…";
        if (line.Contains("Success! App", StringComparison.OrdinalIgnoreCase))
            return "Fichiers serveur DayZ installés avec succès !";
        return line;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
