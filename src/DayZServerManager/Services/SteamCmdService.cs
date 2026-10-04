using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace DayZServerManager.Services;

public record SteamCmdResult(int ExitCode, string Output)
{
    public bool ServerInstallSucceeded => Output.Contains("Success! App '223350' fully installed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Pilote SteamCMD : installation, connexion à Steam et téléchargement du serveur DayZ.</summary>
public sealed class SteamCmdService
{
    public const string DayZServerAppId = "223350";
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

    /// <summary>Une ligne à afficher dans le journal (déjà traduite quand c'est possible).</summary>
    public event Action<string>? Output;

    /// <summary>Progression : étape, pourcentage, octets faits, octets au total.</summary>
    public event Action<string, double, long, long>? Progress;

    /// <summary>Appelé quand SteamCMD demande un code Steam Guard. Renvoie null pour abandonner.</summary>
    public Func<string?>? AskSteamGuardCode { get; set; }

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

    public Task<SteamCmdResult> DownloadServerAsync(string user, string password, CancellationToken ct)
    {
        Directory.CreateDirectory(ServerFolder);
        _lastStage = "";
        var args = $"+force_install_dir {Quote(ServerFolder)} +login {Quote(user)} {Quote(password)} " +
                   $"+app_update {DayZServerAppId} validate +quit";
        return RunAsync(args, ct);
    }

    // ===== Lancement de SteamCMD =====

    private async Task<SteamCmdResult> RunAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(SteamCmdExe, arguments)
        {
            WorkingDirectory = SteamCmdFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var all = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.Start();

        using var registration = ct.Register(() => KillQuietly(process));

        var outputTask = Task.Run(() => ReadOutputAsync(process, all));
        var errorTask = Task.Run(() => ReadErrorsAsync(process, all));
        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync();

        ct.ThrowIfCancellationRequested();
        string text;
        lock (all) text = all.ToString();
        return new SteamCmdResult(process.ExitCode, text);
    }

    private async Task ReadOutputAsync(Process process, StringBuilder all)
    {
        var reader = process.StandardOutput;
        var buffer = new char[4096];
        var pending = new StringBuilder();
        int read;

        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            pending.Append(buffer, 0, read);
            var text = pending.ToString();

            int cut = text.LastIndexOfAny(new[] { '\n', '\r' });
            if (cut >= 0)
            {
                foreach (var line in text[..cut].Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    HandleLine(line, all);
                text = text[(cut + 1)..];
                pending.Clear().Append(text);
            }

            // Les questions de SteamCMD ne finissent pas par un retour à la ligne.
            if (IsCodePrompt(text))
            {
                pending.Clear();
                Output?.Invoke("Steam demande un code Steam Guard.");
                var code = AskSteamGuardCode?.Invoke();
                if (string.IsNullOrWhiteSpace(code))
                {
                    Output?.Invoke("Aucun code saisi : connexion abandonnée.");
                    KillQuietly(process);
                    return;
                }
                await process.StandardInput.WriteLineAsync(code.Trim());
                await process.StandardInput.FlushAsync();
            }
        }

        if (pending.Length > 0) HandleLine(pending.ToString(), all);
    }

    private async Task ReadErrorsAsync(Process process, StringBuilder all)
    {
        string? line;
        while ((line = await process.StandardError.ReadLineAsync()) != null)
            HandleLine(line, all);
    }

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

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Le processus est déjà terminé.
        }
    }
}
