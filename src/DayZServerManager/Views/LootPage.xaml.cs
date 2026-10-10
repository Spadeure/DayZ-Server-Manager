using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

/// <summary>Éditeur visuel du loot : tous les fichiers « types » du serveur (base et dossiers d'économie).</summary>
public partial class LootPage : UserControl
{
    private const double DockWidth = 1060;  // en dessous, la fiche s'ouvre par-dessus la liste
    private const double WideListWidth = 860; // en dessous, zones et rareté passent sous le nom

    private static readonly (string Key, string Label)[] FlagLabels =
    [
        ("count_in_cargo", "Compter dans les sacs"),
        ("count_in_hoarder", "Compter dans les caches"),
        ("count_in_map", "Compter sur la carte"),
        ("count_in_player", "Compter sur les joueurs"),
        ("crafted", "Fabriqué"),
        ("deloot", "Loot dynamique"),
    ];

    private List<LootFile> _files = new();
    private LootFile? _current;
    private LootItem? _selected;
    private string _sortKey = "name";
    private int _sortDir = 1;
    private bool _loading;
    private bool _docked = true;
    private bool _overlayOpen;
    private bool? _wideRows;
    private bool _bulkEditing; // évite de tout recalculer à chaque objet pendant une modification groupée

    // Éléments de la fiche mis à jour sans la reconstruire (pour ne pas perdre la saisie en cours).
    private readonly Dictionary<string, TextBox> _fieldBoxes = new();
    private TextBlock? _lifetimeHint;
    private TextBlock? _restockHint;
    private Border? _warningBox;
    private TextBlock? _xmlPreview;
    private Button? _resetButton;

    public LootPage()
    {
        InitializeComponent();
        UpdateChanges();
        BuildDetail();
    }

    /// <summary>Vrai si des modifications ne sont pas encore enregistrées.</summary>
    public bool HasUnsavedChanges => _files.Any(f => f.ModifiedCount > 0);

    /// <summary>Recharge les fichiers (appelé à chaque ouverture de l'onglet), sauf s'il y a des changements en cours.</summary>
    public async void Refresh()
    {
        if (_loading || HasUnsavedChanges) return;
        _loading = true;
        ShowStatus("Lecture des fichiers de loot…", "MutedBrush");
        EmptyText.Visibility = Visibility.Collapsed;

        try
        {
            var problems = new List<string>();
            var files = await Task.Run(() => LootService.LoadAll(problems));
            foreach (var item in _files.SelectMany(f => f.Items)) item.PropertyChanged -= Item_PropertyChanged;
            _files = files;
            foreach (var item in _files.SelectMany(f => f.Items)) item.PropertyChanged += Item_PropertyChanged;

            var previous = _current?.FullPath;
            _current = _files.FirstOrDefault(f => f.FullPath == previous) ?? _files.FirstOrDefault();
            _selected = null;

            BuildFiles();
            BuildFilters();
            ApplyFilter();
            BuildDetail();
            UpdateChanges();

            if (EconomyService.MissionFolder() == null)
                ShowEmpty("Installe d'abord le serveur (Paramètres → Ouvrir l'installation) : ses fichiers de loot seront alors disponibles.");
            else if (_files.Count == 0)
                ShowEmpty("Aucun fichier types.xml trouvé dans la mission.");

            if (problems.Count > 0) ShowStatus("Fichiers ignorés : " + string.Join(" · ", problems), "WarnBrush");
            else HideStatus();
        }
        catch (Exception ex)
        {
            ShowStatus($"Impossible de lire les fichiers de loot : {ex.Message}", "WarnBrush");
        }
        finally
        {
            _loading = false;
        }
    }

    // ===== Fichiers =====

    private void BuildFiles()
    {
        FilesPanel.Children.Clear();
        foreach (var file in _files)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = file.DisplayPath, FontFamily = new FontFamily("Consolas"), FontSize = 12.5 });
            var count = file.ModifiedCount;
            text.Children.Add(new TextBlock
            {
                Text = $"{file.Label} · {file.Items.Count} objets" + (count > 0 ? $" · {count} modifié(s)" : ""),
                FontSize = 11.5,
                FontWeight = FontWeights.Normal,
                Foreground = Res(count > 0 ? "AccentBrush" : "MutedBrush"),
                Margin = new Thickness(0, 2, 0, 0),
            });

            var button = new RadioButton
            {
                GroupName = "lootfiles",
                Style = (Style)Application.Current.FindResource("ChipButton"),
                Height = double.NaN,
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 8, 8),
                Content = text,
                IsChecked = file == _current,
                ToolTip = file.FullPath,
            };
            var target = file;
            button.Checked += (_, _) =>
            {
                if (_current == target) return;
                _current = target;
                _selected = null;
                ClearChecks();
                ApplyFilter();
                BuildDetail();
            };
            FilesPanel.Children.Add(button);
        }
    }

    /// <summary>Met à jour le nombre d'objets modifiés sous chaque fichier.</summary>
    private void RefreshFileCounts()
    {
        var buttons = FilesPanel.Children.OfType<RadioButton>().ToList();
        for (int i = 0; i < buttons.Count && i < _files.Count; i++)
        {
            if (buttons[i].Content is not StackPanel { Children.Count: 2 } panel || panel.Children[1] is not TextBlock line) continue;
            var file = _files[i];
            var count = file.ModifiedCount;
            line.Text = $"{file.Label} · {file.Items.Count} objets" + (count > 0 ? $" · {count} modifié(s)" : "");
            line.Foreground = Res(count > 0 ? "AccentBrush" : "MutedBrush");
        }
    }

    // ===== Filtres et tri =====

    private void BuildFilters()
    {
        var all = _files.SelectMany(f => f.Items).ToList();
        Fill(CategoryFilter, "Toutes les catégories",
            Union(LootLabels.StandardCategories, all.Select(i => i.Category).Where(c => c.Length > 0)), LootLabels.Category);
        Fill(UsageFilter, "Toutes les zones", Union(LootLabels.StandardUsages, all.SelectMany(i => i.Usage)), LootLabels.Usage);
        Fill(TierFilter, "Toutes les raretés", Union(LootLabels.StandardTiers, all.SelectMany(i => i.Value)), LootLabels.Tier);
    }

    private static IEnumerable<string> Union(IEnumerable<string> standard, IEnumerable<string> found) =>
        standard.Concat(found.Except(standard).OrderBy(x => x)).Distinct();

    private static void Fill(ComboBox combo, string first, IEnumerable<string> values, Func<string, string> label)
    {
        var previous = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = first, Tag = "" });
        foreach (var value in values) combo.Items.Add(new ComboBoxItem { Content = label(value), Tag = value });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == previous) ?? combo.Items[0];
    }

    private static string Selected(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) ApplyFilter();
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        _sortDir = _sortKey == key ? -_sortDir : key == "name" ? 1 : -1;
        _sortKey = key;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_current == null)
        {
            ItemsList.ItemsSource = null;
            TotalText.Text = "";
            return;
        }

        var query = SearchBox.Text.Trim();
        var category = Selected(CategoryFilter);
        var usage = Selected(UsageFilter);
        var tier = Selected(TierFilter);
        var modifiedOnly = ModifiedOnly.IsChecked == true;

        IEnumerable<LootItem> list = _current.Items.Where(i => Matches(i, query, category, usage, tier, modifiedOnly));

        list = _sortKey switch
        {
            "nominal" => _sortDir > 0 ? list.OrderBy(i => i.Nominal) : list.OrderByDescending(i => i.Nominal),
            "min" => _sortDir > 0 ? list.OrderBy(i => i.Min) : list.OrderByDescending(i => i.Min),
            "lifetime" => _sortDir > 0 ? list.OrderBy(i => i.Lifetime) : list.OrderByDescending(i => i.Lifetime),
            _ => _sortDir > 0 ? list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase) : list.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };

        var items = list.ToList();
        ItemsList.ItemsSource = items;
        if (_selected != null && items.Contains(_selected)) ItemsList.SelectedItem = _selected;

        var total = _current.Items.Sum(i => i.Nominal);
        TotalText.Text = $"{items.Count} objet(s) affiché(s) sur {_current.Items.Count} · {total} exemplaires visés sur la carte";
        AllBox.IsChecked = items.Count > 0 && items.All(i => i.IsChecked);

        UpdateMultiplierHint();
        if (items.Count == 0 && _current.Items.Count > 0) ShowEmpty("Aucun objet ne correspond à la recherche.");
        else EmptyText.Visibility = Visibility.Collapsed;
    }

    private static bool Matches(LootItem i, string query, string category, string usage, string tier, bool modifiedOnly) =>
        (query.Length == 0 || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
        (category.Length == 0 || i.Category == category) &&
        (usage.Length == 0 || i.Usage.Contains(usage)) &&
        (tier.Length == 0 || i.Value.Contains(tier)) &&
        (!modifiedOnly || i.IsModified);

    private bool FiltersActive() =>
        SearchBox.Text.Trim().Length > 0 || Selected(CategoryFilter).Length > 0 || Selected(UsageFilter).Length > 0 ||
        Selected(TierFilter).Length > 0 || ModifiedOnly.IsChecked == true;

    /// <summary>Objets concernés par le multiplicateur : ceux affichés, ou tous les fichiers avec les mêmes filtres.</summary>
    private List<LootItem> MultiplierTargets()
    {
        if (AllFilesBox.IsChecked != true) return ItemsList.ItemsSource as List<LootItem> ?? new List<LootItem>();
        var query = SearchBox.Text.Trim();
        var category = Selected(CategoryFilter);
        var usage = Selected(UsageFilter);
        var tier = Selected(TierFilter);
        var modifiedOnly = ModifiedOnly.IsChecked == true;
        return _files.SelectMany(f => f.Items).Where(i => Matches(i, query, category, usage, tier, modifiedOnly)).ToList();
    }

    private void UpdateMultiplierHint()
    {
        var count = MultiplierTargets().Count;
        var where = AllFilesBox.IsChecked == true ? "dans tous les fichiers" : "du fichier affiché";
        MultiplierHint.Text = FiltersActive()
            ? $"S'applique aux {count} objet(s) filtrés {where}."
            : $"S'applique aux {count} objet(s) {where}.";
    }

    private void AllFiles_Click(object sender, RoutedEventArgs e) => UpdateMultiplierHint();

    /// <summary>
    /// x2, x4, x6 : la quantité et le minimum valent N fois ceux du fichier (pas de cumul : x2 puis x4 donne x4).
    /// x1 remet les valeurs du fichier. Chaque objet reste modifiable ensuite à la main.
    /// </summary>
    private void Multiplier_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var factor)) return;
        FocusManager.SetFocusedElement(this, null);
        Keyboard.ClearFocus();

        var items = MultiplierTargets();
        if (items.Count == 0)
        {
            ShowStatus("Aucun objet à modifier.", "WarnBrush");
            return;
        }

        _bulkEditing = true;
        try
        {
            foreach (var item in items)
            {
                item.Nominal = item.FileNominal * factor;
                item.Min = item.FileMin * factor;
            }
        }
        finally { _bulkEditing = false; }

        AfterBulkEdit();
        ShowStatus(factor == 1
            ? $"Quantités remises comme dans le fichier pour {items.Count} objet(s)."
            : $"Loot x{factor} : quantité et minimum multipliés par {factor} pour {items.Count} objet(s). Pense à enregistrer.",
            "CyanBrush");
    }

    private void AfterBulkEdit()
    {
        UpdateChanges();
        RefreshFileCounts();
        if (_selected != null) UpdateDetailValues();
    }

    // ===== Changements =====

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_bulkEditing) return;
        if (e.PropertyName == nameof(LootItem.IsChecked))
        {
            UpdateBulk();
            return;
        }
        UpdateChanges();
        RefreshFileCounts();
        if (sender == _selected) UpdateDetailValues();
    }

    private void UpdateChanges()
    {
        int count = _files.Sum(f => f.ModifiedCount);
        ChangesText.Text = count > 0 ? $"{count} objet(s) modifié(s), non enregistré(s)" : "Aucun changement";
        ChangesText.Foreground = Res(count > 0 ? "AccentBrush" : "MutedBrush");
        SaveButton.IsEnabled = UndoButton.IsEnabled = count > 0;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        _bulkEditing = true;
        try { foreach (var item in _files.SelectMany(f => f.Items).Where(i => i.IsModified).ToList()) item.Reset(); }
        finally { _bulkEditing = false; }
        AfterBulkEdit();
        ApplyFilter();
        BuildDetail();
        ShowStatus("Tous les changements ont été annulés.", "CyanBrush");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        FocusManager.SetFocusedElement(this, null); // valide une cellule en cours de saisie
        Keyboard.ClearFocus();

        var saved = new List<string>();
        try
        {
            foreach (var file in _files.Where(f => f.ModifiedCount > 0))
            {
                var count = LootService.Save(file);
                saved.Add($"{file.DisplayPath} ({count})");
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Impossible d'enregistrer : {ex.Message}", "WarnBrush");
            return;
        }

        RefreshFileCounts();
        UpdateChanges();
        ApplyFilter();
        BuildDetail();
        if (saved.Count == 0) return;
        ShowStatus($"Enregistré dans {string.Join(", ", saved)}. Une copie .bak de l'ancienne version est gardée." +
                   (ServerManager.Instance.IsRunning ? " Redémarre le serveur pour appliquer." : ""), "CyanBrush");
    }

    // ===== Modifications groupées =====

    private void All_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.ItemsSource is not List<LootItem> items) return;
        var check = AllBox.IsChecked == true;
        foreach (var item in items) item.IsChecked = check;
        UpdateBulk();
    }

    private List<LootItem> CheckedItems() => _current?.Items.Where(i => i.IsChecked).ToList() ?? new List<LootItem>();

    private void UpdateBulk()
    {
        var count = CheckedItems().Count;
        BulkBar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BulkCount.Text = $"{count} objet(s) sélectionné(s)";
    }

    private void ClearChecks()
    {
        foreach (var item in _files.SelectMany(f => f.Items)) item.IsChecked = false;
        AllBox.IsChecked = false;
        UpdateBulk();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => ClearChecks();

    private void Multiply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParse(MultiplyBox.Text, out var factor) || factor <= 0)
        {
            ShowStatus("Entre un nombre plus grand que 0, par exemple 1.5 ou 0.5.", "WarnBrush");
            return;
        }
        var items = CheckedItems();
        _bulkEditing = true;
        try
        {
            foreach (var item in items)
            {
                item.Nominal = (int)Math.Round(item.Nominal * factor);
                item.Min = (int)Math.Round(item.Min * factor);
            }
        }
        finally { _bulkEditing = false; }
        AfterBulkEdit();
        ShowStatus($"Quantité et minimum multipliés par {factor.ToString(CultureInfo.CurrentCulture)} pour {items.Count} objet(s).", "CyanBrush");
    }

    private void BulkLifetime_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParse(LifetimeBox.Text, out var hours) || hours <= 0)
        {
            ShowStatus("Entre un nombre d'heures plus grand que 0.", "WarnBrush");
            return;
        }
        var items = CheckedItems();
        _bulkEditing = true;
        try { foreach (var item in items) item.Lifetime = (int)Math.Round(hours * 3600); }
        finally { _bulkEditing = false; }
        AfterBulkEdit();
        ShowStatus($"Durée de vie réglée sur {hours.ToString(CultureInfo.CurrentCulture)} h pour {items.Count} objet(s).", "CyanBrush");
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // ===== Liste =====

    private void Items_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is not LootItem item || item == _selected) return;
        _selected = item;
        BuildDetail();
        if (!_docked) SetOverlay(true);
    }

    /// <summary>Entrée dans une cellule : la valeur est validée tout de suite.</summary>
    private void Items_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        box.SelectAll();
        e.Handled = true;
    }

    // ===== Disposition selon la place disponible =====

    private void Work_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = WorkGrid.ActualWidth;
        bool docked = width >= DockWidth;
        if (docked != _docked)
        {
            _docked = docked;
            if (docked)
            {
                DetailColumn.Width = new GridLength(340);
                Grid.SetColumn(DetailCard, 1);
                DetailCard.HorizontalAlignment = HorizontalAlignment.Stretch;
                DetailCard.Width = double.NaN;
                DetailCard.Margin = new Thickness(14, 0, 0, 0);
                DetailCard.Effect = null;
                DetailCard.BorderBrush = Res("LineBrush");
                DetailCard.Visibility = Visibility.Visible;
            }
            else
            {
                DetailColumn.Width = new GridLength(0);
                Grid.SetColumn(DetailCard, 0);
                DetailCard.HorizontalAlignment = HorizontalAlignment.Right;
                DetailCard.Margin = new Thickness(0);
                DetailCard.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black };
                DetailCard.BorderBrush = Res("AccentBrush");
                SetOverlay(false);
            }
            BuildDetail();
        }
        if (!docked) DetailCard.Width = Math.Min(380, width);

        // Liste : colonnes zones et rareté seulement s'il y a la place.
        var listWidth = docked ? width - 354 : width;
        bool wide = listWidth >= WideListWidth;
        if (wide == _wideRows) return;
        _wideRows = wide;
        ItemsList.ItemTemplate = (DataTemplate)Resources[wide ? "WideRow" : "CompactRow"];
        HeadQty.Width = new GridLength(wide ? 84 : 78);
        HeadMin.Width = new GridLength(wide ? 84 : 78);
        HeadLife.Width = new GridLength(wide ? 76 : 70);
        HeadZones.Width = wide ? new GridLength(1.3, GridUnitType.Star) : new GridLength(0);
        HeadTiers.Width = new GridLength(wide ? 120 : 0);
        ZonesHeader.Visibility = TiersHeader.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetOverlay(bool open)
    {
        _overlayOpen = open;
        if (!_docked) DetailCard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Fiche de l'objet =====

    private void BuildDetail()
    {
        DetailPanel.Children.Clear();
        _fieldBoxes.Clear();
        _lifetimeHint = _restockHint = _xmlPreview = null;
        _warningBox = null;
        _resetButton = null;

        if (!_docked)
        {
            var close = new Button
            {
                Content = "Fermer ✕",
                Style = (Style)Application.Current.FindResource("SmallButton"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 8),
            };
            close.Click += (_, _) => SetOverlay(false);
            DetailPanel.Children.Add(close);
        }

        var item = _selected;
        if (item == null)
        {
            DetailPanel.Children.Add(Section("RÉGLAGES DE L'OBJET"));
            DetailPanel.Children.Add(Muted("Clique sur un objet de la liste pour régler sa quantité, sa durée de vie, ses zones et sa rareté."));
            return;
        }

        DetailPanel.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            Foreground = Res("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        DetailPanel.Children.Add(Muted($"{item.CategoryLabel} · {item.File.DisplayPath}", 2));

        _warningBox = new Border
        {
            Background = Res("WarnSoftBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock
            {
                Text = "Le minimum est plus grand que la quantité : le serveur fera apparaître trop d'objets.",
                Foreground = Res("WarnBrush"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        DetailPanel.Children.Add(_warningBox);

        DetailPanel.Children.Add(Section("QUANTITÉ SUR LA CARTE"));
        DetailPanel.Children.Add(Pair(
            Field(item, "nominal", "Quantité visée", "Nombre maximum sur la carte", digitsOnly: true, out _),
            Field(item, "min", "Minimum", "En dessous, le serveur en refait apparaître", digitsOnly: true, out _)));

        DetailPanel.Children.Add(Section("TEMPS"));
        DetailPanel.Children.Add(Pair(
            Field(item, "lifetime", "Durée de vie (s)", "", digitsOnly: true, out _lifetimeHint),
            Field(item, "restock", "Réapparition (s)", "", digitsOnly: true, out _restockHint)));

        DetailPanel.Children.Add(Section("CONTENU (MUNITIONS, LIQUIDE, BATTERIE…)"));
        DetailPanel.Children.Add(Pair(
            Field(item, "quantmin", "Minimum %", "-1 = sans objet", digitsOnly: false, out _),
            Field(item, "quantmax", "Maximum %", "-1 = sans objet", digitsOnly: false, out _)));

        DetailPanel.Children.Add(Section("CATÉGORIE"));
        var category = new ComboBox { Style = (Style)Application.Current.FindResource("DarkCombo") };
        category.Items.Add(new ComboBoxItem { Content = "Sans catégorie", Tag = "" });
        foreach (var c in Union(LootLabels.StandardCategories, _files.SelectMany(f => f.Items).Select(i => i.Category).Where(c => c.Length > 0)))
            category.Items.Add(new ComboBoxItem { Content = LootLabels.Category(c), Tag = c });
        category.SelectedItem = category.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == item.Category) ?? category.Items[0];
        category.SelectionChanged += (_, _) => item.Category = Selected(category);
        DetailPanel.Children.Add(category);

        DetailPanel.Children.Add(Section("ZONES D'APPARITION"));
        DetailPanel.Children.Add(Picker(Union(LootLabels.StandardUsages, _files.SelectMany(f => f.Items).SelectMany(i => i.Usage)).ToList(),
            LootLabels.Usage, () => item.Usage, list => item.Usage = list, "AccentBrush", "AccentSoftBrush"));

        DetailPanel.Children.Add(Section("RARETÉ (RÉGIONS DE LA CARTE)"));
        DetailPanel.Children.Add(Picker(Union(LootLabels.StandardTiers, _files.SelectMany(f => f.Items).SelectMany(i => i.Value)).ToList(),
            LootLabels.Tier, () => item.Value, list => item.Value = list, "CyanBrush", "CyanSoftBrush"));

        DetailPanel.Children.Add(Section("COMPTAGE"));
        var flags = new WrapPanel();
        foreach (var (key, label) in FlagLabels)
        {
            var box = new CheckBox
            {
                Content = label,
                IsChecked = item.GetFlag(key) == 1,
                Style = (Style)Application.Current.FindResource("DarkCheck"),
                Width = 145,
                Margin = new Thickness(0, 0, 4, 8),
            };
            var flag = key;
            box.Click += (_, _) => item.SetFlag(flag, box.IsChecked == true);
            flags.Children.Add(box);
        }
        DetailPanel.Children.Add(flags);

        DetailPanel.Children.Add(Section("APERÇU DU XML ÉCRIT"));
        _xmlPreview = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Foreground = Res("ConsoleTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        DetailPanel.Children.Add(new Border
        {
            Background = Res("ConsoleBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = _xmlPreview,
        });

        _resetButton = new Button
        {
            Content = "Remettre les valeurs d'origine",
            Style = (Style)Application.Current.FindResource("SmallButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
        };
        _resetButton.Click += (_, _) =>
        {
            item.Reset();
            BuildDetail();
        };
        DetailPanel.Children.Add(_resetButton);

        UpdateDetailValues();
    }

    /// <summary>Remet à jour les valeurs affichées dans la fiche, sans toucher au champ en cours de saisie.</summary>
    private void UpdateDetailValues()
    {
        var item = _selected;
        if (item == null) return;

        foreach (var (key, box) in _fieldBoxes)
        {
            if (!box.IsKeyboardFocused) box.Text = Read(item, key).ToString(CultureInfo.InvariantCulture);
            box.BorderBrush = Res(item.IsFieldModified(key) ? "AccentBrush" : "LineBrush");
            box.Foreground = Res(item.IsFieldModified(key) ? "AccentBrush" : "TextBrush");
        }
        if (_lifetimeHint != null) _lifetimeHint.Text = $"{LootItem.HumanDuration(item.Lifetime)} avant de disparaître au sol";
        if (_restockHint != null) _restockHint.Text = item.Restock > 0 ? $"Attend {LootItem.HumanDuration(item.Restock)}" : "Immédiate";
        if (_warningBox != null) _warningBox.Visibility = item.HasMinWarning ? Visibility.Visible : Visibility.Collapsed;
        if (_xmlPreview != null) _xmlPreview.Text = item.PreviewXml();
        if (_resetButton != null) _resetButton.Visibility = item.IsModified ? Visibility.Visible : Visibility.Collapsed;
    }

    private static int Read(LootItem item, string key) => key switch
    {
        "nominal" => item.Nominal,
        "min" => item.Min,
        "lifetime" => item.Lifetime,
        "restock" => item.Restock,
        "quantmin" => item.QuantMin,
        _ => item.QuantMax,
    };

    private static void Write(LootItem item, string key, int value)
    {
        switch (key)
        {
            case "nominal": item.Nominal = value; break;
            case "min": item.Min = value; break;
            case "lifetime": item.Lifetime = value; break;
            case "restock": item.Restock = value; break;
            case "quantmin": item.QuantMin = value; break;
            default: item.QuantMax = value; break;
        }
    }

    private StackPanel Field(LootItem item, string key, string label, string hint, bool digitsOnly, out TextBlock hintBlock)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 0, 0, 5) });
        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("FieldBox"),
            Height = 34,
            FontSize = 12.5,
            Text = Read(item, key).ToString(CultureInfo.InvariantCulture),
        };
        if (digitsOnly) box.Tag = "number";

        void Commit()
        {
            if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) Write(item, key, v);
            else box.Text = Read(item, key).ToString(CultureInfo.InvariantCulture);
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            box.SelectAll();
        };
        _fieldBoxes[key] = box;
        panel.Children.Add(box);

        hintBlock = new TextBlock { Text = hint, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x7D, 0x76, 0x99)), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(hintBlock);
        return panel;
    }

    private static Grid Pair(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Boutons à cocher (zones, rareté) : un clic ajoute ou retire la valeur.</summary>
    private WrapPanel Picker(List<string> options, Func<string, string> label, Func<IReadOnlyList<string>> get,
        Action<List<string>> set, string onBrush, string onBackground)
    {
        var panel = new WrapPanel();
        foreach (var option in options)
        {
            var button = new Button
            {
                Content = label(option),
                Style = (Style)Application.Current.FindResource("SmallButton"),
                Margin = new Thickness(0, 0, 6, 6),
            };
            void Paint()
            {
                bool on = get().Contains(option);
                button.Background = on ? Res(onBackground) : Brushes.Transparent;
                button.BorderBrush = Res(on ? onBrush : "LineBrush");
                button.Foreground = Res(on ? "TextBrush" : "MutedBrush");
            }
            var value = option;
            button.Click += (_, _) =>
            {
                var current = get().ToList();
                if (current.Contains(value)) current.Remove(value);
                else current.Add(value);
                // Garde l'ordre habituel des options.
                set(options.Where(current.Contains).Concat(current.Except(options)).ToList());
                Paint();
            };
            Paint();
            panel.Children.Add(button);
        }
        return panel;
    }

    // ===== Outils =====

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Res("MutedBrush"),
        Margin = new Thickness(0, 16, 0, 8),
    };

    private static TextBlock Muted(string text, double top = 0) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Res("MutedBrush"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, top, 0, 0),
    };

    private void ShowEmpty(string message)
    {
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
    }

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = Res(brushKey);
        StatusText.Visibility = Visibility.Visible;
    }

    private void HideStatus() => StatusText.Visibility = Visibility.Collapsed;

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
