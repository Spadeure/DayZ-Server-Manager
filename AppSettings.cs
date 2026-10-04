using System.IO;
using System.Text.Json;

namespace DayZServerManager.Services;

/// <summary>Réglages de l'application, enregistrés dans %AppData%\DayZServerManager.</summary>
public class AppSettings
{
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayZServerManager");

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");

    public static AppSettings Current { get; } = Load();

    /// <summary>Dossier principal choisi par l'utilisateur (SteamCMD + serveur).</summary>
    public string ServerFolder { get; set; } = "";

    /// <summary>Dernier nom d'utilisateur Steam utilisé (jamais le mot de passe).</summary>
    public string SteamUser { get; set; } = "";

    /// <summary>Port de jeu du serveur (les 3 ports suivants sont aussi utilisés).</summary>
    public int GamePort { get; set; } = 2302;

    /// <summary>Port de requête Steam (liste des serveurs).</summary>
    public int QueryPort { get; set; } = 27016;

    /// <summary>Port RCon de BattlEye (administration à distance).</summary>
    public int RconPort { get; set; } = 2306;

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Fichier abîmé : on repart de réglages vides.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
