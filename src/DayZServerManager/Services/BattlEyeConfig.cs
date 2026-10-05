using System.IO;

namespace DayZServerManager.Services;

/// <summary>Fichier de configuration BattlEye (mot de passe et port RCon).</summary>
public static class BattlEyeConfig
{
    private const string FileName = "BEServer_x64.cfg";

    /// <summary>
    /// Selon les réglages du serveur, BattlEye lit sa configuration dans Server\battleye
    /// ou dans Server\profiles\BattlEye : on écrit et on cherche dans les deux.
    /// </summary>
    public static string[] Folders =>
    [
        Path.Combine(ServerManager.ServerFolder, "battleye"),
        Path.Combine(ServerManager.ProfilesFolder, "BattlEye"),
    ];

    public static void Write(string password, int port)
    {
        if (string.IsNullOrWhiteSpace(password)) return;
        var lines = new[]
        {
            $"RConPassword {password}",
            $"RConPort {port}",
            "RestrictRCon 0",
        };
        foreach (var folder in Folders)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, FileName), lines);
        }
    }

    /// <summary>Vrai si BattlEye a chargé sa configuration (il crée alors un fichier « _active_ »).</summary>
    public static bool IsActive()
    {
        try
        {
            return Folders.Any(folder =>
                Directory.Exists(folder) && Directory.GetFiles(folder, "BEServer_x64_active_*.cfg").Length > 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Mot de passe RCon : celui des réglages, sinon celui des fichiers BattlEye.</summary>
    public static string ReadPassword()
    {
        if (!string.IsNullOrWhiteSpace(AppSettings.Current.RconPassword)) return AppSettings.Current.RconPassword;
        try
        {
            foreach (var folder in Folders)
            {
                if (!Directory.Exists(folder)) continue;
                // Pendant que le serveur tourne, BattlEye renomme le fichier en BEServer_x64_active_XXXX.cfg.
                foreach (var file in Directory.GetFiles(folder, "BEServer_x64*.cfg"))
                    foreach (var line in File.ReadAllLines(file))
                        if (line.TrimStart().StartsWith("RConPassword", StringComparison.OrdinalIgnoreCase))
                            return line.Trim()["RConPassword".Length..].Trim();
            }
        }
        catch
        {
            // Fichier illisible.
        }
        return "";
    }
}
