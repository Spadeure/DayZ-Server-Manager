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

    /// <summary>Date des fichiers du mod lors de la dernière copie (évite de recopier un mod inchangé).</summary>
    public long SourceStamp { get; set; }
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

    /// <summary>Mot de passe RCon de BattlEye (BattlEye renomme son fichier pendant que le serveur tourne).</summary>
    public string RconPassword { get; set; } = "";

    /// <summary>Prévenir les joueurs en jeu avant un redémarrage programmé.</summary>
    public bool WarnBeforeRestart { get; set; } = true;

    public string DiscordWebhook { get; set; } = "";
    public bool DiscordEnabled { get; set; }

    /// <summary>Paramètres ajoutés à la fin de la ligne de lancement.</summary>
    public string ExtraLaunchArgs { get; set; } = "";

    /// <summary>Utiliser une ligne de lancement écrite à la main à la place de la ligne automatique.</summary>
    public bool UseCustomLaunchLine { get; set; }

    public string CustomLaunchLine { get; set; } = "";

    /// <summary>« stable » ou « experimental ».</summary>
    public string ServerBranch { get; set; } = "stable";

    /// <summary>Met à jour le serveur à chaque démarrage et surveille les nouvelles versions de DayZ.</summary>
    public bool AutoUpdateServer { get; set; }

    /// <summary>Met à jour les mods du Workshop à chaque démarrage du serveur.</summary>
    public bool AutoUpdateMods { get; set; }

    /// <summary>Démarre le serveur dès l'ouverture de l'application.</summary>
    public bool AutoStartServer { get; set; }

    /// <summary>Le bouton Fermer réduit l'application près de l'horloge.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Menu réduit aux icônes.</summary>
    public bool MenuCollapsed { get; set; }

    /// <summary>Noms associés aux SteamID de la whitelist et de la file prioritaire.</summary>
    public Dictionary<string, string> SteamNames { get; set; } = new();

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
