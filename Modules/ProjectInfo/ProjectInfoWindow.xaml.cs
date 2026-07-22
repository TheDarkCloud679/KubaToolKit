using KubaToolKit.Modules.ProjectInfo.Models;
using KubaToolKit.Shared.Services;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KubaToolKit.Modules.ProjectInfo;

public partial class ProjectInfoWindow
    : Window
{
    private readonly ProjectInfoService _projectInfoService = new();
    private readonly ProjectInfoRoot _root;
    private readonly string _profileName;
    private ProjectInfoProject _project;

    private readonly Dictionary<ProjectInfoSection, (Border Card, DataGrid Grid, DataTable Table)>
        _sectionControls = new();

    private readonly Dictionary<ProjectInfoSection, bool> _sectionExpanded = new();

    private readonly List<(ProjectInfoSection Section, int RowIndex, string Column)>
        _searchMatches = new();

    private int _currentMatchIndex = -1;
    private string _lastSearchQuery = "";

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

        SectionsPanel.AllowDrop = true;
        SectionsPanel.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(ProjectInfoSection)) is not ProjectInfoSection draggedSection)
            {
                return;
            }

            _project.Sections.Remove(draggedSection);
            _project.Sections.Add(draggedSection);

            RenderSections();
            Save();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
                e.Handled = true;
            }
        };

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
    OpenProjectFolderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var folderPath = ProjectInfoService.EnsureProjectFolder(_project.Key);

            Logger.Debug($"ProjectInfoWindow: opening files folder '{folderPath}'.");

            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("ProjectInfoWindow: failed to open files folder.", ex);

            MessageBox.Show(ex.ToString(), "Project Info - files folder");
        }
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
            MessageBox.Show($"A section \"{name}\" already exists.", "Project Info");

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

        SectionsPanel.Children.Add(BuildSectionCard(newSection));

        NewSectionNameTextBox.Text = "";

        Save();
    }

    private void
    RenderSections()
    {
        ClearMatchHighlight();

        SectionsPanel.Children.Clear();
        _sectionControls.Clear();

        foreach (var section in _project.Sections)
        {
            SectionsPanel.Children.Add(BuildSectionCard(section));
        }

        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";
        SearchResultsText.Text = "";
    }

    private UIElement
    BuildSectionCard(
        ProjectInfoSection section)
    {
        Border card = null!;
        DataGrid grid = null!;
        StackPanel columnManageRow = null!;

        var isExpanded =
            _sectionExpanded.TryGetValue(section, out var storedExpanded)
            && storedExpanded;

        var outer = new StackPanel();

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dragHandle = new TextBlock
        {
            Text = "⠿",
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to reorder the section"
        };
        Grid.SetColumn(dragHandle, 0);
        dragHandle.MouseLeftButtonDown += (_, e) =>
        {
            DragDrop.DoDragDrop(card, section, DragDropEffects.Move);
            e.Handled = true;
        };

        var toggleButton = new Button
        {
            Content = isExpanded ? "▼" : "▶",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Expand/collapse the section"
        };
        Grid.SetColumn(toggleButton, 1);
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
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Double-click to rename the section"
        };
        Grid.SetColumn(nameText, 2);

        var nameEditBox = new TextBox
        {
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(nameEditBox, 2);

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
                MessageBox.Show($"A section \"{newName}\" already exists.", "Project Info");

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

            nameEditBox.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    nameEditBox.Focus();
                    nameEditBox.SelectAll();
                }));
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
            ToolTip = "New column name"
        };
        Grid.SetColumn(newColumnTextBox, 3);

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

            TryCommitEdit(grid);
            ReplaceSectionCard(section, card);

            newColumnTextBox.Text = "";

            Save();
        };
        Grid.SetColumn(addColumnButton, 4);

        var deleteSectionButton = new Button
        {
            Content = "Delete section",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2)
        };
        deleteSectionButton.Click += (_, __) =>
        {
            if (MessageBox.Show(
                    $"Delete section \"{section.Name}\" and all its data?",
                    "Confirm",
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }

            TryCommitEdit(grid);

            _project.Sections.Remove(section);
            SectionsPanel.Children.Remove(card);
            _sectionControls.Remove(section);
            _sectionExpanded.Remove(section);

            Save();
        };
        Grid.SetColumn(deleteSectionButton, 5);

        header.Children.Add(dragHandle);
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
            ToolTip = "New name for the selected column"
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
                    "A section must keep at least one column.",
                    "Project Info");

                return;
            }

            if (MessageBox.Show(
                    $"Delete column \"{columnName}\" and its data?",
                    "Confirm",
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

        var exportToFileZillaButton = new Button
        {
            Content = "Export to FileZilla",
            Margin = new Thickness(16, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2),
            ToolTip = "Writes an SFTP entry per row into FileZilla's Site Manager (Name/Host columns you pick, shared login/port/key file)."
        };
        exportToFileZillaButton.Click += (_, __) =>
        {
            TryCommitEdit(grid);

            var settings = FileZillaExportWindow.Prompt(
                this,
                section.Columns.ToList(),
                section.FileZillaExport,
                $"{_project.Key} - {section.Name}",
                ProjectInfoService.GetProjectFolderPath(_project.Key));

            if (settings == null)
            {
                return;
            }

            section.FileZillaExport = settings;
            Save();

            var entries =
                section.Rows
                    .Select(row =>
                        new FileZillaSiteManagerService.SiteEntry(
                            row.TryGetValue(settings.NameColumn, out var name) ? name : "",
                            row.TryGetValue(settings.HostColumn, out var host) ? host : ""))
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Host))
                    .ToList();

            if (entries.Count == 0)
            {
                MessageBox.Show(
                    $"No row has a value in the \"{settings.HostColumn}\" column.",
                    "Export to FileZilla");

                return;
            }

            try
            {
                FileZillaSiteManagerService.ExportFolder(
                    settings.FolderName,
                    entries,
                    settings.Username,
                    settings.Port,
                    settings.KeyFilePath);

                MessageBox.Show(
                    $"Exported {entries.Count} site(s) to the \"{settings.FolderName}\" folder in FileZilla's Site Manager.\n\nClose and reopen FileZilla to see them (if FileZilla was already running, it may overwrite this file when it closes -- re-run the export afterwards if so).",
                    "Export to FileZilla");
            }
            catch (Exception ex)
            {
                Logger.Error("ProjectInfoWindow: FileZilla export failed.", ex);

                MessageBox.Show(ex.ToString(), "Export to FileZilla - error");
            }
        };

        columnManageRow.Children.Add(columnSelectCombo);
        columnManageRow.Children.Add(renameColumnTextBox);
        columnManageRow.Children.Add(renameColumnButton);
        columnManageRow.Children.Add(deleteColumnButton);
        columnManageRow.Children.Add(exportToFileZillaButton);

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
            // Cell (search highlight, via SelectedCells) AND row selection
            // (row context menu, via SelectedItem) both need to work --
            // Cell alone throws on SelectedItem, FullRow alone throws on
            // SelectedCells. CellOrRowHeader supports both.
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed
        };

        grid.LoadingRow += (_, e) =>
        {
            e.Row.Header =
                e.Row.Item == CollectionView.NewItemPlaceholder
                    ? ""
                    : (e.Row.GetIndex() + 1).ToString();
        };

        table.RowChanged += (_, __) => SyncAndSave(section, table);
        table.RowDeleted += (_, __) => SyncAndSave(section, table);

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

            TryCommitEdit(grid);

            // DataView.Sort compares the column's values as text (every
            // section column is a free-form string), so "10" sorted before
            // "2" -- sort rows ourselves with a comparer that treats values
            // as numbers when they parse as one, so 1/2/.../10/100 order
            // correctly instead of lexicographically.
            var filledRows =
                section.Rows.Where(r => !string.IsNullOrEmpty(GetCellValue(r, columnName)));

            var emptyRows =
                section.Rows.Where(r => string.IsNullOrEmpty(GetCellValue(r, columnName)));

            // Blanks always go last, asc or desc, rather than leading an
            // ascending sort just because "" compares before any text.
            var sortedRows =
                (ascending
                    ? filledRows.OrderBy(r => GetCellValue(r, columnName), NaturalValueComparer.Instance)
                    : filledRows.OrderByDescending(r => GetCellValue(r, columnName), NaturalValueComparer.Instance))
                    .Concat(emptyRows)
                    .ToList();

            section.Rows = sortedRows;

            // Reorder the bound table in place rather than rebuilding the
            // whole card (BuildSectionCard again): that swapped in a new
            // DataGrid/columns on every click, losing the SortDirection
            // arrow (so toggling asc/desc never worked) and risking the
            // same "index already in use" WPF quirk guarded against for
            // column add/rename/delete. _isSyncing suppresses SyncAndSave
            // while rows are torn down and re-added -- section.Rows above
            // is already the correct final state.
            _isSyncing = true;

            try
            {
                table.Rows.Clear();

                foreach (var row in sortedRows)
                {
                    var dataRow = table.NewRow();

                    foreach (var col in section.Columns)
                    {
                        dataRow[col] = row.TryGetValue(col, out var value) ? value : "";
                    }

                    table.Rows.Add(dataRow);
                }

                table.AcceptChanges();
            }
            finally
            {
                _isSyncing = false;
            }

            foreach (var col in grid.Columns)
            {
                col.SortDirection = null;
            }

            column.SortDirection =
                ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;

            Save();
        };

        // Right-click a row for Insert/Duplicate/Delete. Selects the row
        // under the cursor first (rather than trusting whatever was already
        // selected), and suppresses the menu entirely off a real row (empty
        // area below the rows, or the CanUserAddRows "+" placeholder).
        grid.PreviewMouseRightButtonDown += (_, e) =>
        {
            var row = DataGridSortHelper.FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);

            if (row == null || row.Item == CollectionView.NewItemPlaceholder)
            {
                e.Handled = true;

                return;
            }

            grid.SelectedItem = row.Item;
        };

        var insertAboveItem = new MenuItem { Header = "Insert row above" };
        insertAboveItem.Click += (_, __) =>
        {
            if (grid.SelectedItem is not DataRowView rowView)
            {
                return;
            }

            var index = table.Rows.IndexOf(rowView.Row);

            if (index < 0)
            {
                return;
            }

            TryCommitEdit(grid);
            table.Rows.InsertAt(table.NewRow(), index);
        };

        var insertBelowItem = new MenuItem { Header = "Insert row below" };
        insertBelowItem.Click += (_, __) =>
        {
            if (grid.SelectedItem is not DataRowView rowView)
            {
                return;
            }

            var index = table.Rows.IndexOf(rowView.Row);

            if (index < 0)
            {
                return;
            }

            TryCommitEdit(grid);
            table.Rows.InsertAt(table.NewRow(), index + 1);
        };

        var duplicateItem = new MenuItem { Header = "Duplicate row" };
        duplicateItem.Click += (_, __) =>
        {
            if (grid.SelectedItem is not DataRowView rowView)
            {
                return;
            }

            var index = table.Rows.IndexOf(rowView.Row);

            if (index < 0)
            {
                return;
            }

            TryCommitEdit(grid);

            var newRow = table.NewRow();
            newRow.ItemArray = rowView.Row.ItemArray;
            table.Rows.InsertAt(newRow, index + 1);
        };

        var deleteRowItem = new MenuItem { Header = "Delete row" };
        deleteRowItem.Click += (_, __) =>
        {
            if (grid.SelectedItem is not DataRowView rowView)
            {
                return;
            }

            TryCommitEdit(grid);
            rowView.Row.Delete();
        };

        grid.ContextMenu = new ContextMenu
        {
            Items =
            {
                insertAboveItem,
                insertBelowItem,
                duplicateItem,
                new Separator(),
                deleteRowItem
            }
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
            Child = outer,
            AllowDrop = true
        };

        card.DragEnter += (_, e) =>
        {
            e.Effects =
                e.Data.GetDataPresent(typeof(ProjectInfoSection))
                    ? DragDropEffects.Move
                    : DragDropEffects.None;

            e.Handled = true;
        };

        card.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(ProjectInfoSection)) is not ProjectInfoSection draggedSection
                || draggedSection == section)
            {
                return;
            }

            var oldIndex = _project.Sections.IndexOf(draggedSection);
            var newIndex = _project.Sections.IndexOf(section);

            if (oldIndex < 0 || newIndex < 0)
            {
                return;
            }

            _project.Sections.RemoveAt(oldIndex);

            if (oldIndex < newIndex)
            {
                newIndex--;
            }

            var dropAfter = e.GetPosition(card).Y > card.ActualHeight / 2;

            _project.Sections.Insert(dropAfter ? newIndex + 1 : newIndex, draggedSection);

            RenderSections();
            Save();

            e.Handled = true;
        };

        _sectionControls[section] = (card, grid, table);

        return card;
    }

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
            RenderSections();
        }
    }

    private static string
    GetCellValue(
        Dictionary<string, string> row,
        string columnName) =>
        row.TryGetValue(columnName, out var value) ? value : "";

    /// Numeric comparison when both values parse as a number (so 2 sorts
    /// before 10), falling back to plain text comparison otherwise -- a
    /// section column is free-form and may hold anything.
    private sealed class NaturalValueComparer
        : IComparer<string>
    {
        public static readonly NaturalValueComparer Instance = new();

        public int
        Compare(
            string? a,
            string? b)
        {
            var aIsNumber = double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var aNumber);
            var bIsNumber = double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var bNumber);

            return aIsNumber && bIsNumber
                ? aNumber.CompareTo(bNumber)
                : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

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
            System.Diagnostics.Debug.WriteLine(
                $"ProjectInfo SyncAndSave skipped: {ex}");

            return;
        }
        finally
        {
            _isSyncing = false;
        }

        ScheduleSave();
    }

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
        ClearMatchHighlight();

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

        card.BringIntoView();

        var rowView = table.DefaultView[rowIndex];

        grid.SelectedCells.Clear();
        grid.CurrentCell = new DataGridCellInfo(rowView, column);
        grid.SelectedCells.Add(grid.CurrentCell);
        grid.ScrollIntoView(rowView, column);

        // Focus stays on the search box (not the grid): keyboard focus on
        // a cell means the *next* Enter is swallowed by the DataGrid's own
        // "Enter = move to the row below" instead of advancing the search.
        SearchTextBox.Focus();

        ClearMatchHighlight();

        // ScrollIntoView above hasn't necessarily realized the row's cell
        // containers yet -- selection alone can be subtle when the grid
        // isn't focused, so defer an explicit highlight to the next layout
        // pass once the cell actually exists.
        grid.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (grid.ItemContainerGenerator.ContainerFromIndex(rowIndex) is not DataGridRow row
                    || column.GetCellContent(row)?.Parent is not DataGridCell cell)
                {
                    return;
                }

                _highlightedCell = cell;
                _highlightedCellOriginalBackground = cell.Background;
                cell.Background = SearchMatchBrush;
            }));

        SearchResultsText.Text = $"{index + 1} / {_searchMatches.Count}";
    }

    private DataGridCell? _highlightedCell;
    private Brush? _highlightedCellOriginalBackground;
    private static readonly Brush SearchMatchBrush = CreateSearchMatchBrush();

    private static Brush
    CreateSearchMatchBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE9, 0x8A));
        brush.Freeze();

        return brush;
    }

    private void
    ClearMatchHighlight()
    {
        if (_highlightedCell != null)
        {
            _highlightedCell.Background = _highlightedCellOriginalBackground;
        }

        _highlightedCell = null;
        _highlightedCellOriginalBackground = null;
    }
}
