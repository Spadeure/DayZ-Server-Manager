using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace DayZServerManager.Services;

/// <summary>Télécharge et installe Visual C++ et DirectX sans intervention.</summary>
public static class DependencyInstaller
{
    private const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    private const string DirectXUrl =
        "https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };

    // Dossier de travail sans espaces (C:\ProgramData\DayZServerManager), plus sûr pour les installateurs.
    private static readonly string WorkFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DayZServerManager");

    public static async Task InstallVcRedistAsync(Action<string> log, CancellationToken ct)
    {
        Directory.CreateDirectory(WorkFolder);
        var file = Path.Combine(WorkFolder, "vc_redist.x64.exe");

        log("Téléchargement de Visual C++ 2015–2022…");
        await DownloadAsync(VcRedistUrl, file, ct);

        log("Installation de Visual C++ (une minute environ)…");
        var code = await RunAsync(file, "/install /quiet /norestart", ct);
        TryDelete(file);

        // 0 = installé, 1638 = version plus récente déjà présente, 3010 = redémarrage du PC conseillé.
        if (code == 3010) log("Visual C++ installé. Un redémarrage du PC est conseillé.");
        else if (code is 0 or 1638) log("Visual C++ installé.");
        else throw new InvalidOperationException($"L'installation de Visual C++ a échoué (code {code}).");
    }

    public static async Task InstallDirectXAsync(Action<string> log, CancellationToken ct)
    {
        Directory.CreateDirectory(WorkFolder);
        var file = Path.Combine(WorkFolder, "directx_Jun2010_redist.exe");
        var extractFolder = Path.Combine(WorkFolder, "DirectX");

        log("Téléchargement de DirectX (environ 95 Mo)…");
        await DownloadAsync(DirectXUrl, file, ct);

        if (Directory.Exists(extractFolder)) Directory.Delete(extractFolder, true);
        Directory.CreateDirectory(extractFolder);

        log("Extraction de DirectX…");
        await RunAsync(file, $"/Q /T:{extractFolder}", ct);

        var setup = Path.Combine(extractFolder, "DXSETUP.exe");
        if (!File.Exists(setup)) throw new InvalidOperationException("L'extraction de DirectX a échoué.");

        log("Installation de DirectX…");
        var code = await RunAsync(setup, "/silent", ct);
        TryDelete(file);
        try { Directory.Delete(extractFolder, true); } catch { /* sans importance */ }

        if (code != 0) throw new InvalidOperationException($"L'installation de DirectX a échoué (code {code}).");
        log("DirectX installé.");
    }

    private static async Task DownloadAsync(string url, string file, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(file);
        await response.Content.CopyToAsync(output, ct);
    }

    private static async Task<int> RunAsync(string file, string arguments, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Impossible de lancer {Path.GetFileName(file)}.");

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* déjà terminé */ }
            throw;
        }
        return process.ExitCode;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { /* sans importance */ }
    }
}
