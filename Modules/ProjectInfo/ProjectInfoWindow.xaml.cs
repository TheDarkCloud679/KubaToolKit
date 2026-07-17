using KubaToolKit.Modules.ProjectInfo.Models;
using KubaToolKit.Shared.Services;
using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KubaToolKit.Modules.ProjectInfo;

/// Glossaire libre par projet : sections définies par l'utilisateur
/// (Contacts, Équipements réseau, VPN, ou tout autre nom), chacune avec
/// ses propres colonnes. Enregistré automatiquement à chaque modification
/// dans Config/project-info.json, partagé entre collègues via ce même
/// fichier (copie manuelle, lecteur réseau, ou suivi dans git selon ce que
/// l'équipe préfère). Un "projet" n'est pas forcément un profil AWS
/// unique : le champ Project en haut de la fenêtre permet à Prod/Preprod/
/// Test d'un même client de partager les mêmes informations en portant le
/// même nom.
public partial class ProjectInfoWindow
    : Window
{
    private readonly ProjectInfoService _projectInfoService = new();
    private readonly ProjectInfoRoot _root;
    private readonly string _profileName;
    private ProjectInfoProject _project;

    // Pour retrouver le DataGrid/DataTable d'une section depuis la
    // recherche, sans devoir les faire remonter par un autre canal.
    private readonly Dictionary<ProjectInfoSection, (Border Card, DataGrid Grid, DataTable Table)>
        _sectionControls = new();

    // Absente ici = repliée (comportement par défaut) ; seul un dépli
    // explicite ajoute une entrée, préservée tant que la fenêtre reste
    // ouverte même si la carte de la section est reconstruite.
    private readonly Dictionary<ProjectInfoSection, bool> _sectionExpanded = new();

    private readonly List<(ProjectInfoSection Section, int RowIndex, string Column)>
        _searchMatches = new();

    private int _currentMatchIndex = -1;
    private string _lastSearchQuery = "";

    // Écrire le fichier à chaque cellule validée (donc à chaque tabulation
    // pendant la saisie d'une ligne) causait des micro-gels perceptibles,
    // voire pire en tapant vite. On regroupe les sauvegardes : chaque
    // modification relance ce délai plutôt que d'écrire immédiatement, et
    // seule la dernière modification d'une rafale déclenche une écriture
    // réelle.
    private readonly DispatcherTimer _saveDebounceTimer;
    private bool _isSyncing;

    public ProjectInfoWindow(
        string profileName)
    {
        InitializeComponent();

        _saveDebounceTimer =
            new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };

        _saveDebounceTimer.Tick += (_, __) =>
        {
            _saveDebounceTimer.Stop();
            Save();
        };

        Closing += (_, __) =>
        {
            if (_saveDebounceTimer.IsEnabled)
            {
                _saveDebounceTimer.Stop();
                Save();
            }
        };

        _profileName = profileName;
        _root = _projectInfoService.Load();

        var projectKey = _projectInfoService.ResolveProjectKey(_root, profileName);

        ProjectKeyTextBox.Text = projectKey;

        _project = _projectInfoService.GetOrCreateProject(_root, projectKey);

        SectionPresetCombo.ItemsSource = ProjectInfoService.SectionPresets.Keys.ToList();
        SectionPresetCombo.SelectedIndex = 0;

        UpdateTitle();
        RenderSections();
    }

    private void
    UpdateTitle()
    {
        var text = $"Project Info - {_project.Key} (profile: {_profileName})";

        Title = text;
        TitleTextBlock.Text = text;
    }

    private void
    ProjectKeyTextBox_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        var newKey = ProjectKeyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(newKey))
        {
            ProjectKeyTextBox.Text = _project.Key;

            return;
        }

        if (string.Equals(newKey, _project.Key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _projectInfoService.SetProjectKey(_root, _profileName, newKey);
        _project = _projectInfoService.GetOrCreateProject(_root, newKey);

        UpdateTitle();
        RenderSections();
        Save();
    }

    private void
    AddSectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var preset = SectionPresetCombo.SelectedItem as string ?? "Custom";
        var name = NewSectionNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = preset;
        }

        if (_project.Sections.Any(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Une section \"{name}\" existe déjà.", "Project Info");

            return;
        }

        var columns =
            ProjectInfoService.SectionPresets.TryGetValue(preset, out var presetColumns)
                ? presetColumns.ToList()
                : new List<string> { "Column 1" };

        var newSection =
            new ProjectInfoSection
            {
                Name = name,
                Columns = columns
            };

        _project.Sections.Add(newSection);

        // N'ajoute que la nouvelle carte, ne touche pas aux sections
        // existantes -- reconstruire tout le panneau à chaque changement
        // pouvait détacher un DataGrid pendant qu'il finissait de valider
        // une édition en cours ailleurs, ce que WPF supporte mal.
        SectionsPanel.Children.Add(BuildSectionCard(newSection));

        NewSectionNameTextBox.Text = "";

        Save();
    }

    private void
    RenderSections()
    {
        SectionsPanel.Children.Clear();
        _sectionControls.Clear();

        foreach (var section in _project.Sections)
        {
            SectionsPanel.Children.Add(BuildSectionCard(section));
        }

        // Les correspondances d'une recherche précédente peuvent viser des
        // sections qui n'existent plus (changement de projet, section
        // supprimée) : on repart propre plutôt que de risquer un saut vers
        // une section obsolète.
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";
        SearchResultsText.Text = "";
    }

    private UIElement
    BuildSectionCard(
        ProjectInfoSection section)
    {
        // Déclarés tôt et assignés plus bas : les gestionnaires de clic
        // ci-dessous capturent ces variables (pas leur valeur au moment de
        // la capture), donc elles voient bien la carte/grille finales une
        // fois le clic effectif -- ça permet à "Delete section" et
        // "+ Column" de ne toucher qu'à LEUR propre carte plutôt que de
        // faire un RenderSections() complet qui recréait aussi les
        // DataGrid des autres sections, y compris pendant qu'un autre
        // pouvait être en train de valider une édition (source probable
        // du plantage observé).
        Border card = null!;
        DataGrid grid = null!;
        StackPanel columnManageRow = null!;

        // Repliées, seules l'en-tête et sa ligne d'actions restent
        // visibles -- une section jamais explicitement dépliée reste
        // repliée par défaut.
        var isExpanded =
            _sectionExpanded.TryGetValue(section, out var storedExpanded)
            && storedExpanded;

        var outer = new StackPanel();

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toggleButton = new Button
        {
            Content = isExpanded ? "▼" : "▶",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Déplier/replier la section"
        };
        Grid.SetColumn(toggleButton, 0);
        toggleButton.Click += (_, __) =>
        {
            isExpanded = !isExpanded;
            _sectionExpanded[section] = isExpanded;

            toggleButton.Content = isExpanded ? "▼" : "▶";
            columnManageRow.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            grid.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        };

        var nameText = new TextBlock
        {
            Text = section.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("AccentBrush"),
            // Sans Background, un TextBlock n'est cliquable que là où du
            // texte est effectivement dessiné (pas sur le reste de sa
            // zone, étirée par la colonne Star) -- Transparent rend toute
            // la zone du titre cliquable pour le double-clic de renommage.
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Double-clic pour renommer la section"
        };
        Grid.SetColumn(nameText, 1);

        var nameEditBox = new TextBox
        {
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(nameEditBox, 1);

        void CommitSectionRename()
        {
            var newName = nameEditBox.Text.Trim();

            nameEditBox.Visibility = Visibility.Collapsed;
            nameText.Visibility = Visibility.Visible;

            if (string.IsNullOrWhiteSpace(newName)
                || string.Equals(newName, section.Name, StringComparison.Ordinal))
            {
                nameEditBox.Text = section.Name;

                return;
            }

            if (_project.Sections.Any(s =>
                    s != section && string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Une section \"{newName}\" existe déjà.", "Project Info");

                nameEditBox.Text = section.Name;

                return;
            }

            section.Name = newName;
            nameText.Text = newName;

            Save();
        }

        nameText.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            nameEditBox.Text = section.Name;
            nameText.Visibility = Visibility.Collapsed;
            nameEditBox.Visibility = Visibility.Visible;
            nameEditBox.Focus();
            nameEditBox.SelectAll();
        };

        nameEditBox.LostFocus += (_, __) => CommitSectionRename();

        nameEditBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                nameEditBox.Text = section.Name;
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };

        var newColumnTextBox = new TextBox
        {
            Width = 140,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Nom de la nouvelle colonne"
        };
        Grid.SetColumn(newColumnTextBox, 2);

        var addColumnButton = new Button
        {
            Content = "+ Column",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2)
        };
        addColumnButton.Click += (_, __) =>
        {
            var columnName = newColumnTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(columnName)
                || section.Columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            section.Columns.Add(columnName);

            // Une nouvelle colonne change la structure de la table : cette
            // carte précise doit être reconstruite, mais seulement elle --
            // pas les autres sections, dont les DataGrid n'ont aucune
            // raison d'être touchés pour ce changement.
            TryCommitEdit(grid);
            ReplaceSectionCard(section, card);

            newColumnTextBox.Text = "";

            Save();
        };
        Grid.SetColumn(addColumnButton, 3);

        var deleteSectionButton = new Button
        {
            Content = "Delete section",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2)
        };
        deleteSectionButton.Click += (_, __) =>
        {
            if (MessageBox.Show(
                    $"Supprimer la section \"{section.Name}\" et toutes ses données ?",
                    "Confirmer",
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }

            // Valider une éventuelle édition en cours avant de détacher le
            // DataGrid du visuel : WPF peut se plaindre si on le retire du
            // panneau pendant qu'il essaie encore de valider une cellule
            // ou la ligne d'ajout.
            TryCommitEdit(grid);

            _project.Sections.Remove(section);
            SectionsPanel.Children.Remove(card);
            _sectionControls.Remove(section);
            _sectionExpanded.Remove(section);

            Save();
        };
        Grid.SetColumn(deleteSectionButton, 4);

        header.Children.Add(toggleButton);
        header.Children.Add(nameText);
        header.Children.Add(nameEditBox);
        header.Children.Add(newColumnTextBox);
        header.Children.Add(addColumnButton);
        header.Children.Add(deleteSectionButton);

        outer.Children.Add(header);

        columnManageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed
        };

        var columnSelectCombo = new ComboBox
        {
            ItemsSource = section.Columns.ToList(),
            Width = 140,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        if (columnSelectCombo.Items.Count > 0)
        {
            columnSelectCombo.SelectedIndex = 0;
        }

        var renameColumnTextBox = new TextBox
        {
            Width = 140,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Nouveau nom pour la colonne sélectionnée"
        };

        var renameColumnButton = new Button
        {
            Content = "Rename column",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2)
        };
        renameColumnButton.Click += (_, __) =>
        {
            if (columnSelectCombo.SelectedItem is not string oldName)
            {
                return;
            }

            var newName = renameColumnTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(newName)
                || (!string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase)
                    && section.Columns.Contains(newName, StringComparer.OrdinalIgnoreCase)))
            {
                return;
            }

            TryCommitEdit(grid);

            var columnIndex =
                section.Columns.FindIndex(c => string.Equals(c, oldName, StringComparison.Ordinal));

            if (columnIndex >= 0)
            {
                section.Columns[columnIndex] = newName;
            }

            foreach (var row in section.Rows)
            {
                if (row.Remove(oldName, out var value))
                {
                    row[newName] = value;
                }
            }

            ReplaceSectionCard(section, card);

            Save();
        };

        var deleteColumnButton = new Button
        {
            Content = "Delete column",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2)
        };
        deleteColumnButton.Click += (_, __) =>
        {
            if (columnSelectCombo.SelectedItem is not string columnName)
            {
                return;
            }

            if (section.Columns.Count <= 1)
            {
                MessageBox.Show(
                    "Une section doit garder au moins une colonne.",
                    "Project Info");

                return;
            }

            if (MessageBox.Show(
                    $"Supprimer la colonne \"{columnName}\" et ses données ?",
                    "Confirmer",
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }

            TryCommitEdit(grid);

            section.Columns.Remove(columnName);

            foreach (var row in section.Rows)
            {
                row.Remove(columnName);
            }

            ReplaceSectionCard(section, card);

            Save();
        };

        columnManageRow.Children.Add(columnSelectCombo);
        columnManageRow.Children.Add(renameColumnTextBox);
        columnManageRow.Children.Add(renameColumnButton);
        columnManageRow.Children.Add(deleteColumnButton);

        outer.Children.Add(columnManageRow);

        var table = BuildDataTable(section);

        grid = new DataGrid
        {
            ItemsSource = table.DefaultView,
            AutoGenerateColumns = true,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.All,
            RowHeaderWidth = 34,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 320,
            SelectionUnit = DataGridSelectionUnit.Cell,
            Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed
        };

        // Numéro de ligne dans l'en-tête, pour compter les entrées d'un
        // coup d'œil -- vide pour la ligne "+" d'ajout (CanUserAddRows),
        // qui n'est pas encore une vraie ligne.
        grid.LoadingRow += (_, e) =>
        {
            e.Row.Header =
                e.Row.Item == CollectionView.NewItemPlaceholder
                    ? ""
                    : (e.Row.GetIndex() + 1).ToString();
        };

        table.RowChanged += (_, __) => SyncAndSave(section, table);
        table.RowDeleted += (_, __) => SyncAndSave(section, table);

        // Double-clic sur un en-tête pour trier, comme ailleurs dans
        // l'appli -- DataView.Sort plutôt que DataGridSortHelper (qui
        // suppose une ObservableCollection<T> typée) puisque cette grille
        // est adossée à un DataTable/DataView aux colonnes dynamiques.
        grid.MouseDoubleClick += (_, e) =>
        {
            if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
                is not { Column: { } column })
            {
                return;
            }

            var columnName = column.Header?.ToString();

            if (string.IsNullOrEmpty(columnName))
            {
                return;
            }

            var ascending = column.SortDirection != ListSortDirection.Ascending;

            table.DefaultView.Sort = $"[{columnName}] {(ascending ? "ASC" : "DESC")}";

            foreach (var col in grid.Columns)
            {
                col.SortDirection = null;
            }

            column.SortDirection =
                ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
        };

        outer.Children.Add(grid);

        card = new Border
        {
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)FindResource("RadiusMedium"),
            Background = (Brush)FindResource("SurfaceBrush"),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("CardShadowEffect"),
            Padding = (Thickness)FindResource("CardPadding"),
            Margin = new Thickness(0, 0, 0, 12),
            Child = outer
        };

        // Écrase l'éventuelle entrée précédente pour cette section (cas
        // d'une reconstruction suite à l'ajout d'une colonne) : toujours
        // la dernière carte/grille/table réellement affichées.
        _sectionControls[section] = (card, grid, table);

        return card;
    }

    /// Reconstruit uniquement la carte d'une section (après ajout/
    /// renommage/suppression de colonne) et la remet à sa place dans le
    /// panneau -- jamais RenderSections() pour ce cas, qui toucherait
    /// aussi les DataGrid des autres sections.
    private void
    ReplaceSectionCard(
        ProjectInfoSection section,
        Border card)
    {
        try
        {
            var index = SectionsPanel.Children.IndexOf(card);
            var replacement = BuildSectionCard(section);

            if (index >= 0)
            {
                SectionsPanel.Children[index] = replacement;
            }
            else
            {
                SectionsPanel.Children.Add(replacement);
            }
        }
        catch
        {
            // WPF a déjà refusé ce remplacement ciblé dans de rares cas
            // (index déjà associé à un autre visuel) -- un rendu complet
            // reste toujours valide en repli, le changement est de toute
            // façon déjà enregistré dans le modèle.
            RenderSections();
        }
    }

    /// Force la validation d'une édition de cellule/ligne en cours avant
    /// de retirer le DataGrid du visuel -- sans ça, WPF peut tenter de la
    /// valider lui-même pendant le détachement, ce qui a déjà provoqué un
    /// plantage à la suppression d'une section.
    private void
    TryCommitEdit(
        DataGrid grid)
    {
        try
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        catch
        {
            // Rien d'exploitable à faire ici : la section est de toute
            // façon en train d'être supprimée ou reconstruite.
        }
    }

    private DataTable
    BuildDataTable(
        ProjectInfoSection section)
    {
        var table = new DataTable();

        foreach (var column in section.Columns)
        {
            table.Columns.Add(column, typeof(string));
        }

        foreach (var row in section.Rows)
        {
            var dataRow = table.NewRow();

            foreach (var column in section.Columns)
            {
                dataRow[column] = row.TryGetValue(column, out var value) ? value : "";
            }

            table.Rows.Add(dataRow);
        }

        table.AcceptChanges();

        return table;
    }

    private void
    SyncAndSave(
        ProjectInfoSection section,
        DataTable table)
    {
        // AcceptChanges() plus bas peut re-déclencher cet évènement selon
        // l'état exact de la ligne (observé en pratique, source probable
        // des plantages) : un garde de ré-entrance simple évite toute
        // récursion, même indirecte.
        if (_isSyncing)
        {
            return;
        }

        _isSyncing = true;

        try
        {
            section.Rows =
                table.Rows
                    .Cast<DataRow>()
                    .Where(r =>
                        r.RowState != DataRowState.Deleted
                        && r.RowState != DataRowState.Detached)
                    .Select(r =>
                    {
                        var dict = new Dictionary<string, string>();

                        foreach (var column in section.Columns)
                        {
                            dict[column] = TryReadCell(r, column);
                        }

                        return dict;
                    })
                    .ToList();

            table.AcceptChanges();
        }
        catch (Exception ex)
        {
            // Une ligne en cours d'ajout côté WPF (ligne "+" pas encore
            // pleinement initialisée) peut être dans un état transitoire
            // au moment précis de cet évènement : on l'ignore plutôt que
            // de laisser une exception non gérée planter toute l'appli --
            // la prochaine modification resynchronisera correctement.
            System.Diagnostics.Debug.WriteLine(
                $"ProjectInfo SyncAndSave ignoré : {ex}");

            return;
        }
        finally
        {
            _isSyncing = false;
        }

        ScheduleSave();
    }

    /// Comme SyncAndSave, une ligne peut être dans un état où l'accès à
    /// une colonne précise lève une exception (RowNotInTableException et
    /// apparentées) sans que toute la ligne soit concernée -- protégé
    /// individuellement plutôt que de perdre toute la synchronisation pour
    /// une seule valeur illisible.
    private string
    TryReadCell(
        DataRow row,
        string column)
    {
        try
        {
            return row[column]?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void
    ScheduleSave()
    {
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void
    Save()
    {
        try
        {
            _projectInfoService.Save(_root);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Project Info - save error");
        }
    }

    private void
    SearchTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            SearchPreviousButton_Click(sender, e);
        }
        else
        {
            SearchNextButton_Click(sender, e);
        }
    }

    private void
    SearchNextButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RunSearchIfQueryChanged())
        {
            return;
        }

        GoToNextMatch();
    }

    private void
    SearchPreviousButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RunSearchIfQueryChanged())
        {
            return;
        }

        GoToPreviousMatch();
    }

    /// Ne relance une recherche complète que si le texte a changé depuis
    /// la dernière fois -- sinon Suivant/Précédent se contente d'avancer
    /// dans les résultats déjà trouvés.
    private bool
    RunSearchIfQueryChanged()
    {
        var query = SearchTextBox.Text.Trim();

        if (string.Equals(query, _lastSearchQuery, StringComparison.Ordinal))
        {
            return false;
        }

        _lastSearchQuery = query;
        RunSearch(query);

        return true;
    }

    private void
    RunSearch(
        string query)
    {
        _searchMatches.Clear();
        _currentMatchIndex = -1;

        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResultsText.Text = "";

            return;
        }

        foreach (var section in _project.Sections)
        {
            for (int rowIndex = 0; rowIndex < section.Rows.Count; rowIndex++)
            {
                var row = section.Rows[rowIndex];

                foreach (var column in section.Columns)
                {
                    if (row.TryGetValue(column, out var value)
                        && !string.IsNullOrEmpty(value)
                        && value.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        _searchMatches.Add((section, rowIndex, column));
                    }
                }
            }
        }

        if (_searchMatches.Count == 0)
        {
            SearchResultsText.Text = "No match";

            return;
        }

        GoToNextMatch();
    }

    private void
    GoToNextMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;

        GoToMatch(_currentMatchIndex);
    }

    private void
    GoToPreviousMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        _currentMatchIndex =
            (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;

        GoToMatch(_currentMatchIndex);
    }

    private void
    GoToMatch(
        int index)
    {
        var (section, rowIndex, columnName) = _searchMatches[index];

        if (!_sectionControls.TryGetValue(section, out var controls))
        {
            return;
        }

        var (card, grid, table) = controls;

        // Une section repliée cache son DataGrid : le déplier d'abord,
        // sinon ScrollIntoView/Focus ci-dessous n'ont aucun effet visible.
        if (grid.Visibility != Visibility.Visible)
        {
            _sectionExpanded[section] = true;
            ReplaceSectionCard(section, card);
            (card, grid, table) = _sectionControls[section];
        }

        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
        {
            return;
        }

        var column =
            grid.Columns.FirstOrDefault(c =>
                string.Equals(c.Header?.ToString(), columnName, StringComparison.Ordinal));

        if (column == null)
        {
            return;
        }

        // Amène d'abord la carte dans le champ visible du ScrollViewer
        // parent : un DataGrid jamais rendu (hors du viewport) ignore
        // silencieusement ScrollIntoView tant qu'il n'a pas été mesuré.
        card.BringIntoView();

        var rowView = table.DefaultView[rowIndex];

        grid.SelectedCells.Clear();
        grid.CurrentCell = new DataGridCellInfo(rowView, column);
        grid.SelectedCells.Add(grid.CurrentCell);
        grid.ScrollIntoView(rowView, column);
        grid.Focus();

        SearchResultsText.Text = $"{index + 1} / {_searchMatches.Count}";
    }
}
