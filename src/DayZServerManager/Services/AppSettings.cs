using System.IO;
using System.Text.Json;

namespace DayZServerManager.Services;

/// <summary>Un mod du Steam Workshop installé sur le serveur.</summary>
public class ModEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool ServerSide { get; set; }
}

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

    /// <summary>Redémarre le serveur tout seul s'il plante.</summary>
    public bool AutoRestart { get; set; } = true;

    /// <summary>Redémarrage programmé toutes les X heures.</summary>
    public bool ScheduledRestart { get; set; }

    public int RestartHours { get; set; } = 4;

    /// <summary>Sauvegarde de la persistance à chaque démarrage du serveur.</summary>
    public bool AutoBackup { get; set; } = true;

    /// <summary>Nombre de sauvegardes automatiques gardées.</summary>
    public int BackupKeep { get; set; } = 20;

    /// <summary>Suppression automatique des vieux journaux du serveur.</summary>
    public bool AutoCleanLogs { get; set; } = true;

    public int LogRetentionDays { get; set; } = 14;

    /// <summary>Mods du Workshop installés sur le serveur, dans l'ordre de chargement.</summary>
    public List<ModEntry> Mods { get; set; } = new();

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
