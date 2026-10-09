using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using DayZServerManager.Services;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;

namespace DayZServerManager.Views;

public partial class XmlEditorPage : UserControl
{
    // Coloration XML aux couleurs de l'application.
    private const string HighlightingXml = """
        <SyntaxDefinition name="XmlStryxhost" extensions=".xml" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#6E6A86" />
          <Color name="Declaration" foreground="#8A84A8" />
          <Color name="Tag" foreground="#CB52FF" />
          <Color name="Attribute" foreground="#2FE3F2" />
          <Color name="Value" foreground="#FFD27A" />
          <RuleSet>
            <Span color="Comment" multiline="true"><Begin>&lt;!--</Begin><End>--&gt;</End></Span>
            <Span color="Declaration" multiline="true"><Begin>&lt;\?</Begin><End>\?&gt;</End></Span>
            <Span color="Tag" multiline="true" ruleSet="Tag"><Begin>&lt;/?</Begin><End>/?&gt;</End></Span>
          </RuleSet>
          <RuleSet name="Tag">
            <Span color="Value" multiline="true"><Begin>"</Begin><End>"</End></Span>
            <Span color="Value"><Begin>'</Begin><End>'</End></Span>
            <Rule color="Attribute">[\w\-:\.]+(?=\s*=)</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private static readonly (string Label, string RelativePath)[] MissionFiles =
    [
        ("types.xml", @"db\types.xml"),
        ("events.xml", @"db\events.xml"),
        ("globals.xml", @"db\globals.xml"),
        ("messages.xml", @"db\messages.xml"),
        ("economy.xml", @"db\economy.xml"),
        ("cfgeconomycore.xml", "cfgeconomycore.xml"),
        ("cfgspawnabletypes.xml", "cfgspawnabletypes.xml"),
        ("cfgeventspawns.xml", "cfgeventspawns.xml"),
        ("cfgplayerspawnpoints.xml", "cfgplayerspawnpoints.xml"),
        ("cfgweather.xml", "cfgweather.xml"),
    ];

    private readonly FoldingManager _foldings;
    private readonly XmlFoldingStrategy _foldingStrategy = new();
    private readonly DispatcherTimer _foldTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _path;

    public XmlEditorPage()
    {
        InitializeComponent();

        try
        {
            using var reader = XmlReader.Create(new StringReader(HighlightingXml));
            Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch
        {
            Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("XML");
        }

        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x22, 0xCB, 0x52, 0xFF));
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xCB, 0x52, 0xFF));
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.Caret.CaretBrush = (Brush)Application.Current.FindResource("AccentBrush");

        SearchPanel.Install(Editor.TextArea);
        _foldings = FoldingManager.Install(Editor.TextArea);
        _foldTimer.Tick += (_, _) =>
        {
            _foldTimer.Stop();
            UpdateFoldings();
        };
        Editor.TextChanged += (_, _) =>
        {
            UpdateTitle();
            _foldTimer.Stop();
            _foldTimer.Start();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Save();
                e.Handled = true;
            }
        };

        Editor.IsEnabled = false;
        UpdateTitle();
    }

    /// <summary>Vrai si le fichier ouvert a des modifications non enregistrées.</summary>
    public bool HasUnsavedChanges => _path != null && Editor.IsModified;

    /// <summary>Met à jour les raccourcis vers les fichiers de la mission (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh()
    {
        QuickPanel.Children.Clear();
        string? mission = null;
        try { mission = EconomyService.MissionFolder(); }
        catch { /* serveur pas installé */ }

        if (mission == null)
        {
            QuickTitle.Text = "Installe le serveur pour avoir les raccourcis vers les fichiers de la mission.";
            return;
        }

        QuickTitle.Text = $"Fichiers de la mission ({Path.GetFileName(mission)})";
        foreach (var (label, relative) in MissionFiles)
        {
            var full = Path.Combine(mission, relative);
            if (!File.Exists(full)) continue;
            var button = new Button
            {
                Content = label,
                Style = (Style)Application.Current.FindResource("SmallButton"),
                Margin = new Thickness(0, 0, 8, 6),
                ToolTip = full,
            };
            button.Click += (_, _) => OpenFile(full);
            QuickPanel.Children.Add(button);
        }
    }

    // ===== Ouverture =====

    /// <summary>Ouvre un fichier dans l'éditeur (demande avant d'abandonner des modifications).</summary>
    public void OpenFile(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0) return;
        if (!File.Exists(path))
        {
            ShowStatus($"Fichier introuvable : {path}", "WarnBrush");
            return;
        }
        if (HasUnsavedChanges && !string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) && !ConfirmDiscard()) return;

        try
        {
            Editor.Load(path);
            Editor.IsModified = false;
            Editor.IsEnabled = true;
            _path = path;
            PathBox.Text = path;
            Editor.ScrollToHome();
            UpdateFoldings();
            UpdateTitle();
            var check = Validate();
            if (check.ok) ShowStatus($"Fichier ouvert · {Editor.Document.LineCount} lignes · XML correct.", "CyanBrush");
            else ShowStatus($"Fichier ouvert, mais il contient une erreur ligne {check.line} : {check.message}", "WarnBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Impossible d'ouvrir le fichier : {ex.Message}", "WarnBrush");
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisis un fichier XML",
            Filter = "Fichiers XML (*.xml)|*.xml|Tous les fichiers (*.*)|*.*",
        };
        try
        {
            if (_path != null) dialog.InitialDirectory = Path.GetDirectoryName(_path);
            else if (EconomyService.MissionFolder() is { } mission) dialog.InitialDirectory = mission;
        }
        catch
        {
            // Dossier de départ par défaut.
        }
        if (dialog.ShowDialog() == true) OpenFile(dialog.FileName);
    }

    private void OpenPath_Click(object sender, RoutedEventArgs e) => OpenFile(PathBox.Text);

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OpenFile(PathBox.Text);
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_path == null) return;
        if (Editor.IsModified && !ConfirmDiscard()) return;
        Editor.IsModified = false;
        OpenFile(_path);
    }

    // ===== Vérification et enregistrement =====

    private (bool ok, int line, string message) Validate()
    {
        try
        {
            XDocument.Parse(Editor.Text, LoadOptions.SetLineInfo);
            return (true, 0, "");
        }
        catch (XmlException ex)
        {
            return (false, ex.LineNumber, ex.Message);
        }
    }

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if (_path == null) return;
        var check = Validate();
        if (check.ok)
        {
            ShowStatus("Le XML est correct (balises bien ouvertes et fermées).", "CyanBrush");
            return;
        }
        ShowStatus($"Erreur ligne {check.line} : {check.message}", "WarnBrush");
        GoToLine(check.line);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (_path == null)
        {
            ShowStatus("Ouvre d'abord un fichier.", "WarnBrush");
            return;
        }

        var check = Validate();
        if (!check.ok)
        {
            GoToLine(check.line);
            var answer = MessageBox.Show(Window.GetWindow(this)!,
                $"Le fichier contient une erreur ligne {check.line} :\n{check.message}\n\n" +
                "Le serveur risque de ne pas le lire correctement. Enregistrer quand même ?",
                "Stryxhost Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        try
        {
            // Copie de la version précédente, au cas où.
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", overwrite: true);
            Editor.Save(_path);
            Editor.IsModified = false;
            UpdateTitle();
            ShowStatus(ServerManager.Instance.IsRunning
                ? $"Enregistré (copie de l'ancienne version : {Path.GetFileName(_path)}.bak). Redémarre le serveur pour appliquer."
                : $"Enregistré (copie de l'ancienne version : {Path.GetFileName(_path)}.bak).", "CyanBrush");
        }
        catch (Exception ex)
        {
            ShowStatus($"Impossible d'enregistrer : {ex.Message}", "WarnBrush");
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus();
        ApplicationCommands.Find.Execute(null, Editor.TextArea);
    }

    // ===== Outils =====

    private void GoToLine(int line)
    {
        if (line < 1 || Editor.Document.LineCount == 0) return;
        var target = Editor.Document.GetLineByNumber(Math.Min(line, Editor.Document.LineCount));
        Editor.ScrollToLine(target.LineNumber);
        Editor.Select(target.Offset, target.Length);
        Editor.Focus();
    }

    private void UpdateFoldings()
    {
        if (_path == null) return;
        try { _foldingStrategy.UpdateFoldings(_foldings, Editor.Document); }
        catch { /* XML incomplet pendant la saisie */ }
    }

    private void UpdateTitle()
    {
        FileTitle.Text = _path == null
            ? "AUCUN FICHIER OUVERT"
            : Path.GetFileName(_path).ToUpperInvariant() + (Editor.IsModified ? "  ●  NON ENREGISTRÉ" : "");
        FileTitle.Foreground = (Brush)Application.Current.FindResource(_path != null && Editor.IsModified ? "AccentBrush" : "TextBrush");
    }

    private bool ConfirmDiscard() =>
        MessageBox.Show(Window.GetWindow(this)!,
            "Le fichier ouvert a des modifications non enregistrées. Les abandonner ?",
            "Stryxhost Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)Application.Current.FindResource(brushKey);
    }
}
