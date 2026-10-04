using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace DayZServerManager.Services;

/// <summary>Lit et modifie serverDZ.cfg en gardant le reste du fichier intact.</summary>
public sealed class ServerConfigFile
{
    private string _text;

    private ServerConfigFile(string path, string text)
    {
        FilePath = path;
        _text = text;
    }

    public string FilePath { get; }

    public static ServerConfigFile Load(string path) => new(path, File.ReadAllText(path));

    public string? Get(string key)
    {
        var match = KeyRegex(key).Match(_text);
        if (!match.Success) return null;
        var value = match.Groups[2].Value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        return value;
    }

    public void SetString(string key, string value) => SetRaw(key, "\"" + value.Replace("\"", "'") + "\"");

    public void SetNumber(string key, double value) => SetRaw(key, value.ToString(CultureInfo.InvariantCulture));

    private void SetRaw(string key, string raw)
    {
        var regex = KeyRegex(key);
        if (regex.IsMatch(_text))
        {
            _text = regex.Replace(_text, m => m.Groups[1].Value + raw + m.Groups[3].Value, 1);
            return;
        }

        // Réglage absent : on l'ajoute juste avant « class Missions ».
        var line = $"{key} = {raw};\r\n";
        var missions = Regex.Match(_text, @"^[ \t]*class\s+Missions", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        _text = missions.Success ? _text.Insert(missions.Index, line) : _text.TrimEnd() + "\r\n" + line;
    }

    public void Save()
    {
        // Une copie de l'original est gardée la première fois.
        var backup = FilePath + ".original";
        if (!File.Exists(backup)) File.Copy(FilePath, backup);
        File.WriteAllText(FilePath, _text);
    }

    private static Regex KeyRegex(string key) => new(
        $@"^([ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*)(.*?)([ \t]*;)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);
}
