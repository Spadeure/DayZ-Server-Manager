using System.IO;

namespace DayZServerManager.Services;

/// <summary>Fichier de configuration BattlEye (mot de passe et port RCon).</summary>
public static class BattlEyeConfig
{
    public static string Folder => Path.Combine(ServerManager.ServerFolder, "battleye");
    public static string FilePath => Path.Combine(Folder, "BEServer_x64.cfg");

    public static void Write(string password, int port)
    {
        if (string.IsNullOrWhiteSpace(password)) return;
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(FilePath, new[]
        {
            $"RConPassword {password}",
            $"RConPort {port}",
            "RestrictRCon 0",
        });
    }

    /// <summary>Mot de passe RCon : celui des réglages, sinon celui des fichiers BattlEye.</summary>
    public static string ReadPassword()
    {
        if (!string.IsNullOrWhiteSpace(AppSettings.Current.RconPassword)) return AppSettings.Current.RconPassword;
        try
        {
            if (!Directory.Exists(Folder)) return "";
            // Pendant que le serveur tourne, BattlEye renomme le fichier en BEServer_x64_active_XXXX.cfg.
            foreach (var file in Directory.GetFiles(Folder, "BEServer_x64*.cfg"))
                foreach (var line in File.ReadAllLines(file))
                    if (line.TrimStart().StartsWith("RConPassword", StringComparison.OrdinalIgnoreCase))
                        return line.Trim()["RConPassword".Length..].Trim();
        }
        catch
        {
            // Fichier illisible.
        }
        return "";
    }
}
