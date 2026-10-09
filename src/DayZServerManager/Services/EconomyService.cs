using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DayZServerManager.Services;

/// <summary>Un fichier déclaré dans un dossier « ce » de cfgeconomycore.xml.</summary>
public record CeFile(string Name, string Type, string FullPath)
{
    public bool Exists => File.Exists(FullPath);
}

/// <summary>Un dossier « ce » (ex. : expansion_ce) et les fichiers qu'il déclare.</summary>
public record CeFolder(string Name, string FullPath, List<CeFile> Files)
{
    public bool Exists => Directory.Exists(FullPath);
}

/// <summary>Résultat de la vérification d'un fichier.</summary>
public record CeCheck(bool Ok, string Message);

/// <summary>
/// Gestion des dossiers d'économie supplémentaires (comme expansion_ce) déclarés dans cfgeconomycore.xml.
/// Le serveur lit ces fichiers au démarrage, en plus des fichiers de base du dossier db.
/// </summary>
public static class EconomyService
{
    /// <summary>Types acceptés par DayZ, avec la balise principale attendue dans le fichier.</summary>
    public static readonly (string Type, string Root, string Entry, string Label)[] Types =
    [
        ("types", "types", "type", "Objets (quantités, durée de vie, zones)"),
        ("spawnabletypes", "spawnabletypes", "type", "Accessoires et contenu des objets"),
        ("events", "events", "event", "Événements (véhicules, crashs, animaux…)"),
        ("globals", "variables", "var", "Variables générales de l'économie"),
        ("economy", "economy", "", "Activation des systèmes de l'économie"),
        ("messages", "messages", "message", "Messages en jeu"),
    ];

    private static readonly Regex SafeName = new(@"^[A-Za-z0-9_\-\.]+$");

    // ===== Emplacements =====

    public static string? MissionFolder()
    {
        var configPath = Path.Combine(ServerManager.ServerFolder, "serverDZ.cfg");
        if (!File.Exists(configPath)) return null;
        var template = ServerConfigFile.Load(configPath).Get("template");
        if (string.IsNullOrWhiteSpace(template)) return null;
        var folder = Path.Combine(ServerManager.ServerFolder, "mpmissions", template);
        return Directory.Exists(folder) ? folder : null;
    }

    public static string CorePath(string mission) => Path.Combine(mission, "cfgeconomycore.xml");

    // ===== Lecture =====

    public static List<CeFolder> ReadFolders(string mission)
    {
        var doc = LoadCore(mission);
        var folders = new List<CeFolder>();
        foreach (var ce in doc.Root!.Elements("ce"))
        {
            var name = (string?)ce.Attribute("folder") ?? "";
            if (name.Length == 0) continue;
            var path = Path.Combine(mission, name);
            var files = ce.Elements("file")
                .Select(f => new CeFile((string?)f.Attribute("name") ?? "", (string?)f.Attribute("type") ?? "", Path.Combine(path, (string?)f.Attribute("name") ?? "")))
                .Where(f => f.Name.Length > 0)
                .ToList();
            folders.Add(new CeFolder(name, path, files));
        }
        return folders;
    }

    /// <summary>Dossiers « …ce » présents dans la mission mais pas déclarés.</summary>
    public static List<string> FindUndeclaredFolders(string mission, List<CeFolder> declared)
    {
        var known = new HashSet<string>(declared.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        return Directory.GetDirectories(mission)
            .Select(p => Path.GetFileName(p))
            .OfType<string>()
            .Where(name => !known.Contains(name))
            .Where(name => name.EndsWith("ce", StringComparison.OrdinalIgnoreCase) &&
                           Directory.EnumerateFiles(Path.Combine(mission, name), "*.xml").Any())
            .ToList();
    }

    /// <summary>Fichiers .xml présents dans un dossier déclaré mais absents de la liste.</summary>
    public static List<string> FindUndeclaredFiles(CeFolder folder)
    {
        if (!folder.Exists) return new List<string>();
        var known = new HashSet<string>(folder.Files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        return Directory.GetFiles(folder.FullPath, "*.xml")
            .Select(p => Path.GetFileName(p))
            .OfType<string>()
            .Where(name => !known.Contains(name))
            .ToList();
    }

    // ===== Vérification =====

    /// <summary>Devine le type d'un fichier d'après sa balise principale (sinon d'après son nom).</summary>
    public static string GuessType(string path)
    {
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            reader.MoveToContent();
            var match = Types.FirstOrDefault(t => t.Root.Equals(reader.LocalName, StringComparison.OrdinalIgnoreCase));
            if (match.Type != null) return match.Type;
        }
        catch
        {
            // Fichier illisible : on regarde le nom.
        }

        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.Contains("spawnable")) return "spawnabletypes";
        if (name.Contains("event")) return "events";
        if (name.Contains("global")) return "globals";
        if (name.Contains("economy")) return "economy";
        if (name.Contains("message")) return "messages";
        return "types";
    }

    /// <summary>Vérifie qu'un fichier est un XML correct et qu'il correspond au type déclaré.</summary>
    public static CeCheck Check(CeFile file)
    {
        if (!file.Exists) return new CeCheck(false, "Fichier introuvable dans le dossier");
        var info = Types.FirstOrDefault(t => t.Type == file.Type);
        if (info.Type == null) return new CeCheck(false, $"Type « {file.Type} » inconnu de DayZ");

        XDocument doc;
        try
        {
            doc = XDocument.Load(file.FullPath, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            return new CeCheck(false, $"Erreur XML ligne {ex.LineNumber} : {ex.Message}");
        }
        catch (Exception ex)
        {
            return new CeCheck(false, $"Illisible : {ex.Message}");
        }

        var root = doc.Root!.Name.LocalName;
        if (!root.Equals(info.Root, StringComparison.OrdinalIgnoreCase))
            return new CeCheck(false, $"Balise principale <{root}> : un fichier « {file.Type} » doit commencer par <{info.Root}>");

        if (info.Entry.Length == 0)
            return new CeCheck(true, $"Correct · {doc.Root.Elements().Count()} réglage(s)");

        var entries = doc.Root.Elements(info.Entry).ToList();
        var duplicates = entries
            .Select(e => (string?)e.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);

        var label = info.Entry switch
        {
            "type" => "objet(s)",
            "event" => "événement(s)",
            "var" => "variable(s)",
            _ => "message(s)",
        };
        return duplicates > 0
            ? new CeCheck(true, $"Correct · {entries.Count} {label} · attention : {duplicates} nom(s) en double")
            : new CeCheck(true, $"Correct · {entries.Count} {label}");
    }

    // ===== Modifications =====

    /// <summary>Crée un dossier et le déclare. Ajoute « _ce » à la fin du nom si besoin.</summary>
    public static string CreateFolder(string mission, string name)
    {
        name = name.Trim();
        if (!SafeName.IsMatch(name)) throw new InvalidOperationException("Nom invalide : lettres, chiffres, _ et - uniquement (sans espace).");
        if (!name.EndsWith("_ce", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("-ce", StringComparison.OrdinalIgnoreCase))
            name += "_ce";

        var doc = LoadCore(mission);
        if (doc.Root!.Elements("ce").Any(ce => string.Equals((string?)ce.Attribute("folder"), name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Le dossier « {name} » est déjà déclaré.");

        Directory.CreateDirectory(Path.Combine(mission, name));
        AddChild(doc.Root, new XElement("ce", new XAttribute("folder", name)));
        SaveCore(mission, doc);
        return name;
    }

    /// <summary>Déclare un dossier existant, avec tous ses fichiers .xml (type deviné).</summary>
    public static int DeclareFolder(string mission, string name)
    {
        var doc = LoadCore(mission);
        var ce = new XElement("ce", new XAttribute("folder", name));
        AddChild(doc.Root!, ce);
        int count = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(mission, name), "*.xml").OrderBy(f => f))
        {
            AddChild(ce, new XElement("file", new XAttribute("name", Path.GetFileName(file)), new XAttribute("type", GuessType(file))));
            count++;
        }
        SaveCore(mission, doc);
        return count;
    }

    /// <summary>Copie un fichier dans le dossier (s'il n'y est pas déjà) et le déclare.</summary>
    public static void AddFile(string mission, string folder, string sourcePath, string type)
    {
        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(mission, folder, name);
        Directory.CreateDirectory(Path.Combine(mission, folder));
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, target, overwrite: true);
        Declare(mission, folder, name, type);
    }

    /// <summary>Crée un fichier vide (juste la balise principale) et le déclare.</summary>
    public static string CreateEmptyFile(string mission, string folder, string name, string type)
    {
        name = name.Trim();
        if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) name += ".xml";
        if (!SafeName.IsMatch(name)) throw new InvalidOperationException("Nom invalide : lettres, chiffres, _ et - uniquement (sans espace).");

        var target = Path.Combine(mission, folder, name);
        if (File.Exists(target)) throw new InvalidOperationException($"Le fichier « {name} » existe déjà dans ce dossier.");

        var root = Types.First(t => t.Type == type).Root;
        Directory.CreateDirectory(Path.Combine(mission, folder));
        File.WriteAllText(target,
            $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<{root}>\r\n    <!-- Ajoute tes entrées ici -->\r\n</{root}>\r\n",
            new UTF8Encoding(false));
        Declare(mission, folder, name, type);
        return name;
    }

    public static void Declare(string mission, string folder, string name, string type)
    {
        var doc = LoadCore(mission);
        var ce = FindCe(doc, folder) ?? throw new InvalidOperationException($"Le dossier « {folder} » n'est pas déclaré.");
        var existing = ce.Elements("file").FirstOrDefault(f => string.Equals((string?)f.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) existing.SetAttributeValue("type", type);
        else AddChild(ce, new XElement("file", new XAttribute("name", name), new XAttribute("type", type)));
        SaveCore(mission, doc);
    }

    public static void ChangeType(string mission, string folder, string name, string type) => Declare(mission, folder, name, type);

    public static void RemoveFile(string mission, string folder, string name, bool deleteFromDisk)
    {
        var doc = LoadCore(mission);
        var file = FindCe(doc, folder)?.Elements("file")
            .FirstOrDefault(f => string.Equals((string?)f.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
        if (file != null)
        {
            RemoveWithWhitespace(file);
            SaveCore(mission, doc);
        }
        var path = Path.Combine(mission, folder, name);
        if (deleteFromDisk && File.Exists(path)) File.Delete(path);
    }

    public static void RemoveFolder(string mission, string folder, bool deleteFromDisk)
    {
        var doc = LoadCore(mission);
        var ce = FindCe(doc, folder);
        if (ce != null)
        {
            RemoveWithWhitespace(ce);
            SaveCore(mission, doc);
        }
        var path = Path.Combine(mission, folder);
        if (deleteFromDisk && Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    // ===== Fichier cfgeconomycore.xml =====

    private static XDocument LoadCore(string mission)
    {
        var path = CorePath(mission);
        if (!File.Exists(path)) throw new InvalidOperationException("cfgeconomycore.xml est introuvable dans la mission.");
        try
        {
            var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            if (doc.Root == null || doc.Root.Name.LocalName != "economycore")
                throw new InvalidOperationException("cfgeconomycore.xml ne commence pas par <economycore>.");
            return doc;
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"cfgeconomycore.xml contient une erreur ligne {ex.LineNumber} : {ex.Message}");
        }
    }

    private static void SaveCore(string mission, XDocument doc)
    {
        var path = CorePath(mission);
        // Copie de l'original la première fois, puis une copie de la version précédente à chaque changement.
        var original = path + ".original";
        if (!File.Exists(original)) File.Copy(path, original);
        File.Copy(path, path + ".bak", overwrite: true);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = doc.Declaration == null,
            Indent = false,
        };
        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    private static XElement? FindCe(XDocument doc, string folder) =>
        doc.Root!.Elements("ce").FirstOrDefault(ce => string.Equals((string?)ce.Attribute("folder"), folder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ajoute une balise en respectant l'indentation du fichier.</summary>
    private static void AddChild(XElement parent, XElement child)
    {
        var parentIndent = IndentOf(parent);
        var last = parent.Elements().LastOrDefault();
        if (last != null)
        {
            last.AddAfterSelf(new XText("\n" + IndentOf(last)), child);
        }
        else
        {
            // Balise vide : on retire les espaces éventuels et on reconstruit proprement.
            foreach (var text in parent.Nodes().OfType<XText>().ToList()) text.Remove();
            parent.Add(new XText("\n" + parentIndent + "    "), child, new XText("\n" + parentIndent));
        }
    }

    private static string IndentOf(XElement element)
    {
        if (element.PreviousNode is XText text)
        {
            var value = text.Value;
            int newline = value.LastIndexOf('\n');
            if (newline >= 0) return value[(newline + 1)..];
        }
        return element.Parent == null ? "" : IndentOf(element.Parent) + "    ";
    }

    private static void RemoveWithWhitespace(XElement element)
    {
        if (element.PreviousNode is XText text && string.IsNullOrWhiteSpace(text.Value)) text.Remove();
        element.Remove();
    }
}
