using System.IO;
using System.Text.RegularExpressions;

namespace DayZServerManager.Services;

/// <summary>Copie les mods du Workshop dans le dossier du serveur, avec leurs clés.</summary>
public static class ModService
{
    /// <summary>Accepte un lien du Workshop ou directement le numéro du mod.</summary>
    public static string? ParseWorkshopId(string input)
    {
        var match = Regex.Match(input ?? "", @"(?:id=)?(\d{6,12})");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string ReadModName(string modFolder, string fallback)
    {
        foreach (var file in new[] { "meta.cpp", "mod.cpp" })
        {
            var path = Path.Combine(modFolder, file);
            if (!File.Exists(path)) continue;
            var match = Regex.Match(File.ReadAllText(path), @"^\s*name\s*=\s*""(.*?)""", RegexOptions.Multiline);
            if (match.Success && match.Groups[1].Value.Trim().Length > 0) return match.Groups[1].Value.Trim();
        }
        return fallback;
    }

    public static string MakeFolderName(string name, string id)
    {
        var clean = Regex.Replace(name, @"[^A-Za-z0-9_\-\.]", "");
        return "@" + (clean.Length == 0 ? id : clean);
    }

    public static void InstallToServer(string sourceFolder, string serverFolder, string folderName)
    {
        var target = Path.Combine(serverFolder, folderName);
        CopyDirectory(sourceFolder, target);

        CopyKeys(sourceFolder, serverFolder);
    }

    /// <summary>Copie les clés (.bikey) d'un mod dans le dossier keys du serveur.</summary>
    public static void CopyKeys(string modFolder, string serverFolder)
    {
        var keysFolder = Path.Combine(serverFolder, "keys");
        Directory.CreateDirectory(keysFolder);
        foreach (var key in Directory.EnumerateFiles(modFolder, "*.bikey", SearchOption.AllDirectories))
            File.Copy(key, Path.Combine(keysFolder, Path.GetFileName(key)), overwrite: true);
    }

    /// <summary>Trouve les mods (dossiers @…) copiés à la main dans le serveur et pas encore dans la liste.</summary>
    public static List<ModEntry> DetectManualMods(string serverFolder, IEnumerable<ModEntry> known)
    {
        var knownFolders = new HashSet<string>(known.Select(m => m.Folder), StringComparer.OrdinalIgnoreCase);
        var found = new List<ModEntry>();
        if (!Directory.Exists(serverFolder)) return found;

        foreach (var dir in Directory.GetDirectories(serverFolder, "@*"))
        {
            var folder = Path.GetFileName(dir);
            if (knownFolders.Contains(folder)) continue;
            if (!Directory.Exists(Path.Combine(dir, "addons"))) continue; // ce n'est pas un mod

            try { CopyKeys(dir, serverFolder); } catch { /* clés illisibles */ }
            found.Add(new ModEntry
            {
                Id = "",
                Name = ReadModName(dir, folder.TrimStart('@')),
                Folder = folder,
                Enabled = true,
            });
        }
        return found;
    }

    /// <summary>Copie un mod dans le serveur seulement s'il a changé depuis la dernière copie. Renvoie true si copié.</summary>
    public static bool SyncMod(ModEntry mod, string sourceFolder, string serverFolder, bool force = false)
    {
        if (!Directory.Exists(sourceFolder)) return false;
        var stamp = SourceStamp(sourceFolder);
        var target = Path.Combine(serverFolder, mod.Folder);
        if (!force && stamp == mod.SourceStamp && Directory.Exists(target)) return false;

        InstallToServer(sourceFolder, serverFolder, mod.Folder);
        mod.SourceStamp = stamp;
        return true;
    }

    private static long SourceStamp(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => File.GetLastWriteTimeUtc(file).Ticks)
            .DefaultIfEmpty(0)
            .Max();

    public static void RemoveFromServer(string serverFolder, string folderName)
    {
        var target = Path.Combine(serverFolder, folderName);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }
}
