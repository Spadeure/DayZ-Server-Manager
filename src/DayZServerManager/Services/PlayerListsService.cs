using System.IO;
using System.Text.RegularExpressions;

namespace DayZServerManager.Services;

/// <summary>Whitelist (whitelist.txt) et file prioritaire (priority.txt) du serveur, par SteamID64.</summary>
public static class PlayerListsService
{
    private static readonly Regex SteamIdRegex = new(@"\b(7656\d{13})\b");

    public static string WhitelistPath => Path.Combine(ServerManager.ProfilesFolder, "whitelist.txt");
    public static string PriorityPath => Path.Combine(ServerManager.ProfilesFolder, "priority.txt");

    public static bool IsValidSteamId(string id) => Regex.IsMatch(id.Trim(), @"^7656\d{13}$");

    public static List<string> Read(string path)
    {
        if (!File.Exists(path)) return new List<string>();
        return SteamIdRegex.Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    public static void WriteWhitelist(IEnumerable<string> ids)
    {
        Directory.CreateDirectory(ServerManager.ProfilesFolder);
        var names = AppSettings.Current.SteamNames;
        File.WriteAllLines(WhitelistPath, ids.Select(id =>
            names.TryGetValue(id, out var name) && name.Length > 0 ? $"{id}\t//{name}" : id));
    }

    public static void WritePriority(IEnumerable<string> ids)
    {
        Directory.CreateDirectory(ServerManager.ProfilesFolder);
        var list = ids.ToList();
        File.WriteAllText(PriorityPath, list.Count == 0 ? "" : string.Join(";", list) + ";");
    }

    /// <summary>La whitelist n'est appliquée que si « enableWhitelist = 1 » est dans serverDZ.cfg.</summary>
    public static bool IsWhitelistEnabled()
    {
        var path = Path.Combine(ServerManager.ServerFolder, "serverDZ.cfg");
        return File.Exists(path) && ServerConfigFile.Load(path).Get("enableWhitelist") == "1";
    }

    public static void SetWhitelistEnabled(bool enabled)
    {
        var path = Path.Combine(ServerManager.ServerFolder, "serverDZ.cfg");
        if (!File.Exists(path)) throw new InvalidOperationException("serverDZ.cfg est introuvable.");
        var config = ServerConfigFile.Load(path);
        config.SetNumber("enableWhitelist", enabled ? 1 : 0);
        config.Save();
    }
}
