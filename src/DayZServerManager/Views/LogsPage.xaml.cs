using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public record LogLine(string Text, Brush Brush, bool IsError);

public partial class LogsPage : UserControl
{
    private const int MaxLines = 20000;
    private const long InitialReadBytes = 3 * 1024 * 1024;

    private readonly List<LogLine> _all = new();
    private ObservableCollection<LogLine> _visible = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Brush _normalBrush;
    private readonly Brush _errorBrush;
    private readonly Brush _warningBrush;

    private LogKind _kind = LogKind.Rpt;
    private string? _file;
    private long _position;
    private string _partial = "";

    public LogsPage()
    {
        InitializeComponent();

        _normalBrush = (Brush)Application.Current.FindResource("ConsoleTextBrush");
        _errorBrush = (Brush)Application.Current.FindResource("WarnBrush");
        _warningBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x6A));
        _warningBrush.Freeze();

        LinesList.ItemsSource = _visible;
        CleanToggle.IsChecked = AppSettings.Current.AutoCleanLogs;
        DaysBox.Text = AppSettings.Current.LogRetentionDays.ToString();

        _timer.Tick += (_, _) => Tail();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _timer.Start();
            else _timer.Stop();
        };

        RptChip.IsChecked = true;
    }

    /// <summary>Recharge le journal (appelé à chaque ouverture de l'onglet).</summary>
    public void Refresh() => LoadLatest();

    // ===== Choix et lecture =====

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out LogKind kind)) return;
        _kind = kind;
        if (IsLoaded) LoadLatest();
    }

    private void LoadLatest()
    {
        var files = LogService.GetFiles(_kind);
        if (files.Count == 0)
        {
            _file = null;
            _all.Clear();
            _partial = "";
            Rebuild();
            FileText.Text = _kind == LogKind.App
                ? "Aucune erreur enregistrée par l'application."
                : "Aucun journal pour le moment : il apparaîtra au premier démarrage du serveur.";
            ErrorsText.Text = "";
            return;
        }

        _file = files[0].FullName;
        LoadFile();
    }

    private void LoadFile()
    {
        if (_file == null) return;
        _all.Clear();
        _partial = "";

        try
        {
            using var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - InitialReadBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            _position = stream.Length;

            // Si on commence au milieu du fichier, la première ligne est incomplète.
            if (start > 0)
            {
                int firstBreak = text.IndexOf('\n');
                text = firstBreak >= 0 ? text[(firstBreak + 1)..] : "";
            }
            AddText(text, rebuild: true);
        }
        catch (Exception ex)
        {
            FileText.Text = $"Impossible de lire le journal : {ex.Message}";
        }
        UpdateFileInfo();
    }

    private void Tail()
    {
        if (_file == null)
        {
            LoadLatest();
            return;
        }

        // Le serveur crée un nouveau journal à chaque démarrage : on passe dessus automatiquement.
        if (FollowToggle.IsChecked == true)
        {
            var newest = LogService.GetFiles(_kind).FirstOrDefault();
            if (newest != null && !string.Equals(newest.FullName, _file, StringComparison.OrdinalIgnoreCase))
            {
                _file = newest.FullName;
                LoadFile();
                return;
            }
        }

        try
        {
            var info = new FileInfo(_file);
            if (!info.Exists)
            {
                LoadLatest();
                return;
            }
            if (info.Length < _position)
            {
                LoadFile();
                return;
            }
            if (info.Length == _position) return;

            using var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(_position, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            _position = stream.Length;
            AddText(text, rebuild: false);
            UpdateFileInfo();
        }
        catch
        {
            // Fichier momentanément inaccessible : on réessaie au prochain passage.
        }
    }

    private void AddText(string text, bool rebuild)
    {
        text = _partial + text;
        var lines = text.Split('\n');
        _partial = text.EndsWith('\n') ? "" : lines[^1];
        int count = text.EndsWith('\n') ? lines.Length : lines.Length - 1;

        var filter = SearchBox.Text.Trim();
        var added = new List<LogLine>();
        for (int i = 0; i < count; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Trim().Length == 0) continue;
            var logLine = Classify(line);
            _all.Add(logLine);
            added.Add(logLine);
        }

        bool trimmed = false;
        if (_all.Count > MaxLines)
        {
            _all.RemoveRange(0, _all.Count - MaxLines);
            trimmed = true;
        }

        if (rebuild || trimmed)
        {
            Rebuild();
        }
        else
        {
            foreach (var line in added)
                if (Matches(line, filter)) _visible.Add(line);
            UpdateCounts();
        }

        if (FollowToggle.IsChecked == true && _visible.Count > 0)
            LinesList.ScrollIntoView(_visible[^1]);
    }

    private LogLine Classify(string line)
    {
        var lower = line.ToLowerInvariant();
        if (lower.Contains("error") || lower.Contains("exception") || lower.Contains("crash") ||
            lower.Contains("(e)") || lower.Contains("!!!") || lower.Contains("erreur"))
            return new LogLine(line, _errorBrush, true);
        if (lower.Contains("warning") || lower.Contains("(w)"))
            return new LogLine(line, _warningBrush, false);
        return new LogLine(line, _normalBrush, false);
    }

    // ===== Recherche =====

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private static bool Matches(LogLine line, string filter) =>
        filter.Length == 0 || line.Text.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void Rebuild()
    {
        var filter = SearchBox.Text.Trim();
        _visible = new ObservableCollection<LogLine>(_all.Where(l => Matches(l, filter)));
        LinesList.ItemsSource = _visible;
        UpdateCounts();
        if (_visible.Count > 0) LinesList.ScrollIntoView(_visible[^1]);
    }

    private void UpdateCounts()
    {
        var filter = SearchBox.Text.Trim();
        CountText.Text = filter.Length > 0
            ? $"{_visible.Count} ligne(s) trouvée(s) sur {_all.Count}"
            : $"{_all.Count} lignes";
        int errors = _all.Count(l => l.IsError);
        ErrorsText.Text = errors > 0 ? $"{errors} erreur(s)" : "";
    }

    private void UpdateFileInfo()
    {
        if (_file == null) return;
        try
        {
            var info = new FileInfo(_file);
            FileText.Text = $"{info.Name} · {FormatSize(info.Length)} · modifié le {info.LastWriteTime:dd/MM à HH:mm:ss}";
        }
        catch
        {
            FileText.Text = Path.GetFileName(_file);
        }
    }

    // ===== Boutons et options =====

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_file == null || !File.Exists(_file)) return;
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_file}\"") { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _kind == LogKind.App ? AppSettings.DataFolder : ServerManager.ProfilesFolder;
        if (!Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void CleanOptions_Changed(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Current;
        settings.AutoCleanLogs = CleanToggle.IsChecked == true;
        if (int.TryParse(DaysBox.Text.Trim(), out var days) && days >= 0 && days <= 365)
            settings.LogRetentionDays = days;
        else
            DaysBox.Text = settings.LogRetentionDays.ToString();
        settings.Save();
    }

    private async void CleanNow_Click(object sender, RoutedEventArgs e)
    {
        CleanOptions_Changed(sender, e);
        var (count, bytes) = await Task.Run(() => LogService.CleanOldLogs());
        var days = AppSettings.Current.LogRetentionDays;
        FileText.Text = count > 0
            ? $"{count} journal(aux) supprimé(s), {FormatSize(bytes)} libérés."
            : days == 0
                ? "Aucun journal à supprimer (ceux utilisés par le serveur en ligne sont gardés)."
                : $"Aucun journal de plus de {days} jours à supprimer.";
        if (_file != null && !File.Exists(_file)) LoadLatest();
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.0} Go"
        : bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:0.0} Mo"
        : $"{Math.Max(1, bytes / 1024)} Ko";
}
