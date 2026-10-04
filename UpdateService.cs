using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DayZServerManager.Services;

public record ReleaseInfo(Version Version, string Notes, DateTime Published, string DownloadUrl, long Size);

/// <summary>Vérifie les nouvelles versions sur GitHub et met l'application à jour.</summary>
public static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Spadeure/DayZ-Server-Manager/releases/latest";
    private const string AssetName = "DayZServerManager.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DayZServerManager");
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>Dernière version publiée, ou null s'il n'y en a pas encore.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(LatestReleaseUrl, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return null;
        var version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));

        var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
            ? body.GetString() ?? ""
            : "";
        var published = root.TryGetProperty("published_at", out var date) && date.TryGetDateTime(out var d)
            ? d.ToLocalTime()
            : DateTime.Now;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                continue;
            return new ReleaseInfo(version, notes, published,
                asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.GetProperty("size").GetInt64());
        }

        // Version publiée mais l'exe n'est pas encore joint.
        return null;
    }

    /// <summary>Télécharge la nouvelle version, remplace l'exe actuel et lance la nouvelle version.</summary>
    public static async Task DownloadAndInstallAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
    {
        var exePath = Environment.ProcessPath
                      ?? throw new InvalidOperationException("Emplacement de l'application introuvable.");
        var newPath = exePath + ".new";
        var oldPath = exePath + ".old";

        using (var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.Size;

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(newPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress.Report(done * 100.0 / total);
            }
        }

        if (new FileInfo(newPath).Length < 1_000_000)
            throw new InvalidDataException("Le fichier téléchargé semble incomplet.");

        // Windows autorise à renommer un exe en cours d'exécution, pas à l'écraser.
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(exePath, oldPath);
        try
        {
            File.Move(newPath, exePath);
        }
        catch
        {
            File.Move(oldPath, exePath);
            throw;
        }

        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }

    /// <summary>Supprime l'ancienne version laissée par une mise à jour.</summary>
    public static void CleanupOldVersion()
    {
        var exePath = Environment.ProcessPath;
        if (exePath == null) return;
        var oldPath = exePath + ".old";

        Task.Run(async () =>
        {
            // L'ancienne version met parfois quelques secondes à se fermer.
            for (int attempt = 0; attempt < 10 && File.Exists(oldPath); attempt++)
            {
                try
                {
                    File.Delete(oldPath);
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
        });
    }
}
