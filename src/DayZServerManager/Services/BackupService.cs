using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DayZServerManager.Services;

public record BackupInfo(string FilePath, DateTime Date, string Reason, string Map, long Size)
{
    public string FileName => Path.GetFileName(FilePath);

    public string ReasonLabel => Reason switch
    {
        "auto" => "Automatique",
        "manuel" => "Manuelle",
        "avant-restauration" => "Avant restauration",
        "avant-wipe" => "Avant wipe",
        _ => Reason,
    };
}

/// <summary>Sauvegarde, restauration et remise à zéro de la persistance (bases, objets, véhicules…).</summary>
public static class BackupService
{
    private static readonly Regex NameRegex = new(@"^(.*)_(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})_(.*)$");

    public static string BackupFolder => Path.Combine(AppSettings.Current.ServerFolder, "Backups");

    /// <summary>Carte utilisée et dossier de persistance (mpmissions\carte\storage_X).</summary>
    public static (string Template, string StorageFolder) FindStorage()
    {
        var configPath = Path.Combine(ServerManager.ServerFolder, "serverDZ.cfg");
        if (!File.Exists(configPath)) throw new InvalidOperationException("serverDZ.cfg est introuvable : installe d'abord le serveur.");

        var config = ServerConfigFile.Load(configPath);
        var template = config.Get("template") ?? "dayzOffline.chernarusplus";
        var instance = config.Get("instanceId") ?? "1";
        return (template, Path.Combine(ServerManager.ServerFolder, "mpmissions", template, $"storage_{instance}"));
    }

    public static string MapName(string template)
    {
        var name = template.Contains('.') ? template[(template.LastIndexOf('.') + 1)..] : template;
        return name.ToLowerInvariant() switch
        {
            "chernarusplus" => "Chernarus",
            "enoch" => "Livonia",
            "sakhal" => "Sakhal",
            _ => name,
        };
    }

    /// <summary>Crée une sauvegarde. Renvoie null s'il n'y a encore rien à sauvegarder.</summary>
    public static BackupInfo? Create(string reason)
    {
        var (template, storage) = FindStorage();
        if (!Directory.Exists(storage) || !Directory.EnumerateFileSystemEntries(storage).Any()) return null;

        Directory.CreateDirectory(BackupFolder);
        var file = Path.Combine(BackupFolder, $"{MapName(template)}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{reason}.zip");
        if (File.Exists(file)) File.Delete(file);
        ZipFile.CreateFromDirectory(storage, file, CompressionLevel.Fastest, includeBaseDirectory: false);

        PruneAutomatic();
        return Parse(file);
    }

    public static List<BackupInfo> List()
    {
        if (!Directory.Exists(BackupFolder)) return new List<BackupInfo>();
        return Directory.GetFiles(BackupFolder, "*.zip")
            .Select(Parse)
            .OrderByDescending(b => b.Date)
            .ToList();
    }

    /// <summary>Remplace la persistance actuelle par une sauvegarde (une sauvegarde de sécurité est faite avant).</summary>
    public static void Restore(BackupInfo backup)
    {
        var (_, storage) = FindStorage();
        Create("avant-restauration");
        if (Directory.Exists(storage)) Directory.Delete(storage, recursive: true);
        Directory.CreateDirectory(storage);
        ZipFile.ExtractToDirectory(backup.FilePath, storage);
    }

    /// <summary>Remet la carte à zéro (une sauvegarde de sécurité est faite avant).</summary>
    public static void Wipe()
    {
        var (_, storage) = FindStorage();
        Create("avant-wipe");
        if (Directory.Exists(storage)) Directory.Delete(storage, recursive: true);
    }

    public static void Delete(BackupInfo backup)
    {
        if (File.Exists(backup.FilePath)) File.Delete(backup.FilePath);
    }

    private static void PruneAutomatic()
    {
        var keep = Math.Max(1, AppSettings.Current.BackupKeep);
        foreach (var old in List().Where(b => b.Reason == "auto").Skip(keep))
        {
            try { File.Delete(old.FilePath); } catch { /* fichier utilisé */ }
        }
    }

    private static BackupInfo Parse(string file)
    {
        var info = new FileInfo(file);
        var match = NameRegex.Match(Path.GetFileNameWithoutExtension(file));
        if (match.Success && DateTime.TryParseExact(match.Groups[2].Value, "yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return new BackupInfo(file, date, match.Groups[3].Value, match.Groups[1].Value, info.Length);

        return new BackupInfo(file, info.LastWriteTime, "manuel", "?", info.Length);
    }
}
