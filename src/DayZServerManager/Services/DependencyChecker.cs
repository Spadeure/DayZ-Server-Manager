using System.IO;
using Microsoft.Win32;

namespace DayZServerManager.Services;

public record DependencyStatus(string Name, string Description, bool Installed);

/// <summary>Vérifie la présence des éléments nécessaires au serveur DayZ.</summary>
public static class DependencyChecker
{
    public static List<DependencyStatus> Check(string mainFolder) =>
    [
        new("Visual C++ 2015–2022 (x64)", "Bibliothèques requises par DayZServer_x64.exe", IsVcRedistInstalled()),
        new("DirectX Runtime (juin 2010)", "Composants DirectX hérités", IsDirectXInstalled()),
        new("SteamCMD", "Outil de téléchargement de Valve", IsSteamCmdInstalled(mainFolder)),
    ];

    public static string SteamCmdFolder(string mainFolder) => Path.Combine(mainFolder, "SteamCMD");

    private static bool IsVcRedistInstalled()
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
                if (key?.GetValue("Installed") is int installed && installed == 1) return true;
            }
            catch
            {
                // Clé illisible : on essaie l'autre vue du registre.
            }
        }
        return false;
    }

    private static bool IsDirectXInstalled() =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "d3dx9_43.dll"));

    private static bool IsSteamCmdInstalled(string mainFolder) =>
        !string.IsNullOrWhiteSpace(mainFolder) &&
        File.Exists(Path.Combine(SteamCmdFolder(mainFolder), "steamcmd.exe"));
}
