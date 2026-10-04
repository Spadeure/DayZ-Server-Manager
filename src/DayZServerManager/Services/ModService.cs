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

        var keysFolder = Path.Combine(serverFolder, "keys");
        Directory.CreateDirectory(keysFolder);
        foreach (var key in Directory.EnumerateFiles(sourceFolder, "*.bikey", SearchOption.AllDirectories))
            File.Copy(key, Path.Combine(keysFolder, Path.GetFileName(key)), overwrite: true);
    }

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
