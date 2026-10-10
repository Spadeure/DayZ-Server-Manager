using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DayZServerManager.Services;

/// <summary>Un fichier « types » (types.xml de base ou fichier d'un dossier d'économie) et ses objets.</summary>
public sealed class LootFile
{
    public required string FullPath { get; init; }
    public required string DisplayPath { get; init; }
    public required string Label { get; init; }
    public required XDocument Document { get; init; }
    public List<LootItem> Items { get; } = new();

    public int ModifiedCount => Items.Count(i => i.IsModified);
}

/// <summary>Un objet du loot (une balise &lt;type&gt;), modifiable depuis l'interface.</summary>
public sealed class LootItem : INotifyPropertyChanged
{
    public static readonly string[] FlagNames =
        ["count_in_cargo", "count_in_hoarder", "count_in_map", "count_in_player", "crafted", "deloot"];

    private sealed record Snapshot(int Nominal, int Min, int Lifetime, int Restock, int QuantMin, int QuantMax,
        string Category, string Usage, string Value, string Flags);

    private Snapshot _original;
    private int _nominal, _min, _lifetime, _restock, _quantMin, _quantMax;
    private string _category = "";
    private List<string> _usage = new(), _value = new();
    private Dictionary<string, int> _flags = new();
    private bool _isChecked;

    public LootItem(LootFile file, XElement element)
    {
        File = file;
        Element = element;
        Name = (string?)element.Attribute("name") ?? "";
        _nominal = ReadInt("nominal", 0);
        _min = ReadInt("min", 0);
        _lifetime = ReadInt("lifetime", 0);
        _restock = ReadInt("restock", 0);
        _quantMin = ReadInt("quantmin", -1);
        _quantMax = ReadInt("quantmax", -1);
        _category = (string?)element.Element("category")?.Attribute("name") ?? "";
        _usage = element.Elements("usage").Select(u => (string?)u.Attribute("name")).OfType<string>().ToList();
        _value = element.Elements("value").Select(v => (string?)v.Attribute("name")).OfType<string>().ToList();
        var flags = element.Element("flags");
        foreach (var flag in FlagNames)
            _flags[flag] = int.TryParse((string?)flags?.Attribute(flag), out var f) ? f : 0;
        _original = Take();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LootFile File { get; }
    public XElement Element { get; }
    public string Name { get; }

    // ===== Valeurs modifiables =====

    public int Nominal { get => _nominal; set => Set(ref _nominal, Math.Max(0, value)); }
    public int Min { get => _min; set => Set(ref _min, Math.Max(0, value)); }
    public int Lifetime { get => _lifetime; set => Set(ref _lifetime, Math.Max(0, value)); }
    public int Restock { get => _restock; set => Set(ref _restock, Math.Max(0, value)); }
    public int QuantMin { get => _quantMin; set => Set(ref _quantMin, Math.Max(-1, value)); }
    public int QuantMax { get => _quantMax; set => Set(ref _quantMax, Math.Max(-1, value)); }
    public string Category { get => _category; set => Set(ref _category, value); }

    public IReadOnlyList<string> Usage
    {
        get => _usage;
        set { _usage = value.ToList(); Changed(); }
    }

    public IReadOnlyList<string> Value
    {
        get => _value;
        set { _value = value.ToList(); Changed(); }
    }

    public int GetFlag(string name) => _flags.TryGetValue(name, out var v) ? v : 0;

    public void SetFlag(string name, bool on)
    {
        _flags[name] = on ? 1 : 0;
        Changed();
    }

    /// <summary>Case cochée pour les modifications groupées (pas enregistré dans le fichier).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    // ===== Affichage =====

    public bool IsModified => Take() != _original;
    public bool NominalModified => _nominal != _original.Nominal;
    public bool MinModified => _min != _original.Min;
    public bool LifetimeModified => _lifetime != _original.Lifetime;
    public bool HasMinWarning => _min > _nominal;
    public string LifetimeText => HumanDuration(_lifetime);
    public string CategoryLabel => LootLabels.Category(_category);
    public IReadOnlyList<string> UsageLabels => _usage.Select(LootLabels.Usage).ToList();
    public IReadOnlyList<string> TierLabels => _value.Select(v => v.Replace("Tier", "T")).ToList();

    public bool IsFieldModified(string field) => field switch
    {
        "nominal" => _nominal != _original.Nominal,
        "min" => _min != _original.Min,
        "lifetime" => _lifetime != _original.Lifetime,
        "restock" => _restock != _original.Restock,
        "quantmin" => _quantMin != _original.QuantMin,
        "quantmax" => _quantMax != _original.QuantMax,
        _ => false,
    };

    public static string HumanDuration(int seconds) =>
        seconds >= 86400 ? $"{seconds / 86400.0:0.#} j"
        : seconds >= 3600 ? $"{seconds / 3600.0:0.#} h"
        : seconds > 0 ? $"{Math.Max(1, seconds / 60)} min"
        : "0";

    /// <summary>La balise telle qu'elle sera écrite (pour l'aperçu).</summary>
    public string PreviewXml()
    {
        var copy = new XElement(Element);
        ApplyTo(copy, force: true);
        foreach (var text in copy.DescendantNodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList())
            text.Remove();
        return copy.ToString(SaveOptions.None);
    }

    // ===== Annuler / enregistrer =====

    public void Reset()
    {
        _nominal = _original.Nominal;
        _min = _original.Min;
        _lifetime = _original.Lifetime;
        _restock = _original.Restock;
        _quantMin = _original.QuantMin;
        _quantMax = _original.QuantMax;
        _category = _original.Category;
        _usage = Split(_original.Usage);
        _value = Split(_original.Value);
        foreach (var pair in _original.Flags.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=');
            _flags[parts[0]] = int.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        Changed();
    }

    /// <summary>Écrit les changements dans la balise XML (seulement ce qui a changé).</summary>
    public void ApplyToElement() => ApplyTo(Element, force: false);

    /// <summary>Après un enregistrement, les valeurs actuelles deviennent les valeurs d'origine.</summary>
    public void MarkSaved()
    {
        _original = Take();
        Changed();
    }

    private void ApplyTo(XElement element, bool force)
    {
        var o = _original;
        if (force || _nominal != o.Nominal) SetValue(element, "nominal", _nominal);
        if (force || _lifetime != o.Lifetime) SetValue(element, "lifetime", _lifetime);
        if (force || _restock != o.Restock) SetValue(element, "restock", _restock);
        if (force || _min != o.Min) SetValue(element, "min", _min);
        if (force || _quantMin != o.QuantMin) SetValue(element, "quantmin", _quantMin);
        if (force || _quantMax != o.QuantMax) SetValue(element, "quantmax", _quantMax);

        if (force || FlagsText() != o.Flags)
        {
            var flags = element.Element("flags");
            if (flags == null) XmlLayout.AddChild(element, flags = new XElement("flags"));
            foreach (var name in FlagNames) flags.SetAttributeValue(name, GetFlag(name));
        }

        if (force || _category != o.Category)
        {
            var category = element.Element("category");
            if (_category.Length == 0) { if (category != null) XmlLayout.Remove(category); }
            else if (category == null) XmlLayout.AddChild(element, new XElement("category", new XAttribute("name", _category)));
            else category.SetAttributeValue("name", _category);
        }

        if (force || string.Join(',', _usage) != o.Usage) ReplaceNamed(element, "usage", _usage);
        if (force || string.Join(',', _value) != o.Value) ReplaceNamed(element, "value", _value);
    }

    /// <summary>Remplace les balises &lt;usage name&gt; / &lt;value name&gt; (celles avec « user » sont gardées).</summary>
    private static void ReplaceNamed(XElement element, string tag, List<string> names)
    {
        foreach (var old in element.Elements(tag).Where(e => e.Attribute("name") != null).ToList()) XmlLayout.Remove(old);
        foreach (var name in names) XmlLayout.AddChild(element, new XElement(tag, new XAttribute("name", name)));
    }

    private static void SetValue(XElement element, string tag, int value)
    {
        var child = element.Element(tag);
        if (child != null) child.Value = value.ToString(CultureInfo.InvariantCulture);
        else XmlLayout.AddChild(element, new XElement(tag, value.ToString(CultureInfo.InvariantCulture)));
    }

    // ===== Outils =====

    private int ReadInt(string tag, int fallback) =>
        int.TryParse(Element.Element(tag)?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private string FlagsText() => string.Join(';', FlagNames.Select(f => $"{f}={GetFlag(f)}"));

    private Snapshot Take() => new(_nominal, _min, _lifetime, _restock, _quantMin, _quantMax, _category,
        string.Join(',', _usage), string.Join(',', _value), FlagsText());

    private static List<string> Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed();
    }

    /// <summary>Prévient l'interface : toutes les valeurs affichées peuvent avoir changé.</summary>
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>Noms en français des catégories, zones et raretés de DayZ.</summary>
public static class LootLabels
{
    public static readonly string[] StandardUsages =
    [
        "Military", "Police", "Medic", "Firefighter", "Hunting", "Farm", "Village", "Town", "Industrial",
        "Coast", "School", "Office", "Prison", "Lunapark", "SeasonalEvent", "ContaminatedArea", "Historical",
    ];

    public static readonly string[] StandardTiers = ["Tier1", "Tier2", "Tier3", "Tier4"];

    public static readonly string[] StandardCategories =
        ["weapons", "clothes", "food", "tools", "containers", "explosives", "vehiclesparts", "lootdispatch"];

    public static string Usage(string usage) => usage switch
    {
        "Military" => "Militaire",
        "Police" => "Police",
        "Medic" => "Médical",
        "Firefighter" => "Pompiers",
        "Hunting" => "Chasse",
        "Farm" => "Ferme",
        "Village" => "Village",
        "Town" => "Ville",
        "Industrial" => "Industriel",
        "Coast" => "Côte",
        "School" => "École",
        "Office" => "Bureaux",
        "Prison" => "Prison",
        "Lunapark" => "Fête foraine",
        "SeasonalEvent" => "Événement",
        "ContaminatedArea" => "Zone contaminée",
        "Historical" => "Historique",
        _ => usage,
    };

    public static string Tier(string tier) => tier switch
    {
        "Tier1" => "Tier1 · Côte",
        "Tier2" => "Tier2 · Intérieur",
        "Tier3" => "Tier3 · Nord",
        "Tier4" => "Tier4 · Militaire nord",
        _ => tier,
    };

    public static string Category(string category) => category switch
    {
        "weapons" => "Armes",
        "clothes" => "Vêtements",
        "food" => "Nourriture",
        "tools" => "Outils",
        "containers" => "Contenants",
        "explosives" => "Explosifs",
        "vehiclesparts" => "Pièces de véhicule",
        "lootdispatch" => "Divers",
        "" => "Sans catégorie",
        _ => category,
    };
}

/// <summary>Ajout et retrait de balises en gardant l'indentation du fichier.</summary>
internal static class XmlLayout
{
    public static void AddChild(XElement parent, XElement child)
    {
        var last = parent.Elements().LastOrDefault();
        if (last != null)
        {
            last.AddAfterSelf(new XText("\n" + IndentOf(last)), child);
            return;
        }
        var parentIndent = IndentOf(parent);
        foreach (var text in parent.Nodes().OfType<XText>().ToList()) text.Remove();
        parent.Add(new XText("\n" + parentIndent + "    "), child, new XText("\n" + parentIndent));
    }

    public static void Remove(XElement element)
    {
        if (element.PreviousNode is XText text && string.IsNullOrWhiteSpace(text.Value)) text.Remove();
        element.Remove();
    }

    private static string IndentOf(XElement element)
    {
        if (element.PreviousNode is XText text)
        {
            int newline = text.Value.LastIndexOf('\n');
            if (newline >= 0) return text.Value[(newline + 1)..];
        }
        return element.Parent == null ? "" : IndentOf(element.Parent) + "    ";
    }
}

/// <summary>Lecture et enregistrement de tous les fichiers « types » du serveur.</summary>
public static class LootService
{
    /// <summary>Le types.xml de base, puis les fichiers « types » des dossiers d'économie (expansion_ce…).</summary>
    public static List<LootFile> LoadAll(List<string> problems)
    {
        var files = new List<LootFile>();
        var mission = EconomyService.MissionFolder();
        if (mission == null) return files;

        TryLoad(files, problems, Path.Combine(mission, "db", "types.xml"), @"db\types.xml", "Fichier de base");

        List<CeFolder> folders;
        try { folders = EconomyService.ReadFolders(mission); }
        catch (Exception ex)
        {
            problems.Add($"cfgeconomycore.xml : {ex.Message}");
            return files;
        }

        foreach (var folder in folders)
            foreach (var file in folder.Files.Where(f => f.Type == "types" && f.Exists))
                TryLoad(files, problems, file.FullPath, $@"{folder.Name}\{file.Name}", folder.Name);

        return files;
    }

    private static void TryLoad(List<LootFile> files, List<string> problems, string path, string display, string label)
    {
        if (!File.Exists(path)) return;
        try
        {
            var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var file = new LootFile { FullPath = path, DisplayPath = display, Label = label, Document = doc };
            foreach (var type in doc.Root!.Elements("type").Where(t => t.Attribute("name") != null))
                file.Items.Add(new LootItem(file, type));
            files.Add(file);
        }
        catch (XmlException ex)
        {
            problems.Add($"{display} : erreur ligne {ex.LineNumber} (corrige-la dans l'Éditeur XML)");
        }
        catch (Exception ex)
        {
            problems.Add($"{display} : {ex.Message}");
        }
    }

    /// <summary>Écrit les objets modifiés dans leur fichier (copie .bak de l'ancienne version). Renvoie le nombre d'objets.</summary>
    public static int Save(LootFile file)
    {
        var modified = file.Items.Where(i => i.IsModified).ToList();
        if (modified.Count == 0) return 0;

        foreach (var item in modified) item.ApplyToElement();

        File.Copy(file.FullPath, file.FullPath + ".bak", overwrite: true);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = file.Document.Declaration == null,
            Indent = false,
        };
        using (var writer = XmlWriter.Create(file.FullPath, settings))
            file.Document.Save(writer);

        foreach (var item in modified) item.MarkSaved();
        return modified.Count;
    }
}
