using System.IO;

namespace DayZServerManager.Services;

public enum LogKind
{
    Rpt,
    Adm,
    Script,
    App,
}

/// <summary>Trouve et nettoie les journaux du serveur et de l'application.</summary>
public static class LogService
{
    private static readonly string[] CleanPatterns = ["*.RPT", "*.ADM", "*.log", "*.mdmp"];

    public static string AppLogFile => Path.Combine(AppSettings.DataFolder, "erreurs.log");

    /// <summary>Fichiers d'un type de journal, du plus récent au plus ancien.</summary>
    public static List<FileInfo> GetFiles(LogKind kind)
    {
        if (kind == LogKind.App)
            return File.Exists(AppLogFile) ? new List<FileInfo> { new(AppLogFile) } : new List<FileInfo>();

        var folder = ServerManager.ProfilesFolder;
        if (string.IsNullOrWhiteSpace(AppSettings.Current.ServerFolder) || !Directory.Exists(folder))
            return new List<FileInfo>();

        var pattern = kind switch
        {
            LogKind.Rpt => "*.RPT",
            LogKind.Adm => "*.ADM",
            _ => "script*.log",
        };
        return new DirectoryInfo(folder).GetFiles(pattern).OrderByDescending(f => f.LastWriteTime).ToList();
    }

    /// <summary>Supprime les journaux plus vieux que le nombre de jours choisi (0 = tous).</summary>
    public static (int Count, long Bytes) CleanOldLogs()
    {
        var folder = ServerManager.ProfilesFolder;
        if (string.IsNullOrWhiteSpace(AppSettings.Current.ServerFolder) || !Directory.Exists(folder)) return (0, 0);

        // 0 jour = tous les journaux (sauf ceux encore utilisés par le serveur).
        var days = Math.Max(0, AppSettings.Current.LogRetentionDays);
        var limit = days == 0 ? DateTime.MaxValue : DateTime.Now.AddDays(-days);
        int count = 0;
        long bytes = 0;
        foreach (var pattern in CleanPatterns)
        {
            foreach (var file in new DirectoryInfo(folder).GetFiles(pattern))
            {
                if (file.LastWriteTime >= limit) continue;
                try
                {
                    var size = file.Length;
                    file.Delete();
                    count++;
                    bytes += size;
                }
                catch
                {
                    // Fichier encore utilisé par le serveur.
                }
            }
        }
        return (count, bytes);
    }
}
