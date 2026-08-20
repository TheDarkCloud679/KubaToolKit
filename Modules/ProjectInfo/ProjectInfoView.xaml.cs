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
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using KubaToolKit.Shared.Windows;

namespace KubaToolKit.Modules.ProjectInfo;

public partial class ProjectInfoView
    : UserControl
{
    private readonly ProjectInfoService _projectInfoService = new();
    private ProjectInfoRoot _root = null!;
    private string _profileName = "";
    private ProjectInfoProject _project = null!;

    private readonly Dictionary<ProjectInfoSection, (Border Card, DataGrid Grid, DataTable Table)>
        _sectionControls = new();

    private readonly Dictionary<ProjectInfoSection, bool> _sectionExpanded = new();

    private readonly List<(ProjectInfoSection Section, int RowIndex, string Column)>
        _searchMatches = new();

    private int _currentMatchIndex = -1;
    private string _lastSearchQuery = "";

    private readonly DispatcherTimer _saveDebounceTimer;
    private bool _isSyncing;

    public ProjectInfoView(
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

        LoadProfile(profileName);
    }

    // Called both from the constructor and whenever the Atlassian module's
    // profile picker selection changes -- flushes whatever the previous
    // profile had pending, then swaps in the new profile's project.
    public void
    ChangeProfile(
        string profileName)
    {
        if (string.Equals(profileName, _profileName, StringComparison.Ordinal))
        {
            return;
        }

        FlushPendingSave();
        LoadProfile(profileName);
    }

    private void
    LoadProfile(
        string profileName)
    {
        _profileName = profileName;
        _root = _projectInfoService.Load();

        var projectKey = _projectInfoService.ResolveProjectKey(_root, profileName);

        _project = _projectInfoService.LoadProject(projectKey);

        UpdateTitle();
        RenderSections();
    }

    // UserControl has no Closing event -- the host (AtlassianSearchView)
    // calls this before switching the profile picker or leaving the tab,
    // so a pending debounced save isn't lost.
    public void
    FlushPendingSave()
    {
        if (_saveDebounceTimer.IsEnabled)
        {
            _saveDebounceTimer.Stop();
            Save();
        }
    }

    private void
    UpdateTitle()
    {
        TitleTextBlock.Text = $"Project Info - {_project.Key} (Profile: {_profileName})";
    }

    private void
    OpenProjectFolderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var folderPath = ProjectInfoService.EnsureProjectFolder(_project.Key);

            Logger.Debug($"ProjectInfoView: opening files folder '{folderPath}'.");

            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("ProjectInfoView: failed to open files folder.", ex);

            AppMessageBox.Show(ex.ToString(), "Project Info - files folder");
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
            AppMessageBox.Show($"A section \"{name}\" already exists.", "Project Info");

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
        StackPanel collapsibleContent = null!;

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

        var toggleIcon = CreateIconPath("M0,0 L4,4 L8,0", size: 8, strokeThickness: 1.6);
        toggleIcon.Margin = new Thickness(0);
        toggleIcon.RenderTransformOrigin = new Point(0.5, 0.5);

        // The raw chevron geometry points down -- 0deg is that as-is
        // (expanded, "pointing at" the now-visible content below), and
        // -90deg rotates it to point right (collapsed, "pointing at" the
        // content hidden to the side).
        var toggleIconRotate = new RotateTransform(isExpanded ? 0 : -90);
        toggleIcon.RenderTransform = toggleIconRotate;

        var toggleButton = new Button
        {
            Style = (Style)FindResource("IconChevronButtonStyle"),
            Content = toggleIcon,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Expand/collapse the section"
        };
        Grid.SetColumn(toggleButton, 1);
        toggleButton.Click += (_, __) =>
        {
            isExpanded = !isExpanded;
            _sectionExpanded[section] = isExpanded;

            toggleIconRotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(isExpanded ? 0 : -90, new Duration(TimeSpan.FromSeconds(0.16))));

            AnimateCardExpand(collapsibleContent, isExpanded);
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
                AppMessageBox.Show($"A section \"{newName}\" already exists.", "Project Info");

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
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Content = BuildIconTextContent("M7,2 L7,12 M2,7 L12,7", "Column"),
            Margin = new Thickness(6, 0, 0, 0)
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
            Style = (Style)FindResource("SecondaryDangerButtonStyle"),
            Content = BuildIconTextContent(TrashIconData, "Delete section"),
            Margin = new Thickness(6, 0, 0, 0)
        };
        deleteSectionButton.Click += (_, __) =>
        {
            if (AppMessageBox.Show(
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

        // Holds everything the toggle button shows/hides (the column
        // management row and the DataGrid). Kept as one element so its own
        // Height can be animated directly on expand/collapse -- same
        // principle as the Dashboard's RDS/EC2 sections, minus the
        // MaxHeight workaround that needed there (RowDefinition.Height is
        // a GridLength and isn't animatable; a plain FrameworkElement's
        // Height is a double and is).
        collapsibleContent = new StackPanel { ClipToBounds = true };

        if (!isExpanded)
        {
            collapsibleContent.Visibility = Visibility.Collapsed;
        }

        outer.Children.Add(collapsibleContent);

        columnManageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
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
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Content = "Rename column",
            Margin = new Thickness(6, 0, 0, 0)
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
            Style = (Style)FindResource("SecondaryDangerButtonStyle"),
            Content = BuildIconTextContent(TrashIconData, "Delete column"),
            Margin = new Thickness(6, 0, 0, 0)
        };
        deleteColumnButton.Click += (_, __) =>
        {
            if (columnSelectCombo.SelectedItem is not string columnName)
            {
                return;
            }

            if (section.Columns.Count <= 1)
            {
                AppMessageBox.Show(
                    "A section must keep at least one column.",
                    "Project Info");

                return;
            }

            if (AppMessageBox.Show(
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
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Content = "Export to FileZilla",
            Margin = new Thickness(16, 0, 0, 0),
            ToolTip = "Writes an SFTP entry per row into FileZilla's Site Manager (Name/Host columns you pick, shared login/port/key file)."
        };
        exportToFileZillaButton.Click += (_, __) =>
        {
            TryCommitEdit(grid);

            var settings = FileZillaExportWindow.Prompt(
                Window.GetWindow(this),
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
                AppMessageBox.Show(
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

                AppMessageBox.Show(
                    $"Exported {entries.Count} site(s) to the \"{settings.FolderName}\" folder in FileZilla's Site Manager.\n\nClose and reopen FileZilla to see them (if FileZilla was already running, it may overwrite this file when it closes -- re-run the export afterwards if so).",
                    "Export to FileZilla");
            }
            catch (Exception ex)
            {
                Logger.Error("ProjectInfoView: FileZilla export failed.", ex);

                AppMessageBox.Show(ex.ToString(), "Export to FileZilla - error");
            }
        };

        columnManageRow.Children.Add(columnSelectCombo);
        columnManageRow.Children.Add(renameColumnTextBox);
        columnManageRow.Children.Add(renameColumnButton);
        columnManageRow.Children.Add(deleteColumnButton);
        columnManageRow.Children.Add(exportToFileZillaButton);

        collapsibleContent.Children.Add(columnManageRow);

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
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
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

        collapsibleContent.Children.Add(grid);

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

    private const string TrashIconData =
        "M2,4 L12,4 M5,4 L5,2 L9,2 L9,4 M3,4 L3.8,13 L10.2,13 L11,4 M6,6.5 L6,10.5 M8,6.5 L8,10.5";

    // Stroke binds to the Button's own Foreground (like the main nav's
    // icons), so it recolors automatically with whatever hover-state
    // Foreground swap that button's style applies -- no per-trigger
    // wiring needed here.
    private static System.Windows.Shapes.Path
    CreateIconPath(
        string data,
        double size = 14,
        double strokeThickness = 1.4)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = strokeThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        };

        BindingOperations.SetBinding(
            path,
            Shape.StrokeProperty,
            new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });

        return path;
    }

    private static StackPanel
    BuildIconTextContent(
        string iconData,
        string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };

        content.Children.Add(CreateIconPath(iconData));
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        return content;
    }

    // Same principle as the Dashboard's RDS/EC2 sections: toggling
    // Visibility alone snaps the card to its new size instantly, with
    // only whatever's newly revealed doing any visible easing. Height
    // here is a plain FrameworkElement double (unlike RowDefinition's
    // GridLength), so it can be animated directly -- measure the natural
    // size without letting it paint, then animate real height from old
    // to new.
    private static void
    AnimateCardExpand(
        FrameworkElement wrapper,
        bool expand)
    {
        var oldHeight = wrapper.ActualHeight;

        if (expand)
        {
            wrapper.Visibility = Visibility.Visible;
        }

        wrapper.Height = double.NaN;
        wrapper.UpdateLayout();

        var naturalHeight = wrapper.ActualHeight;

        wrapper.Height = oldHeight;

        var targetHeight = expand ? naturalHeight : 0;

        var heightAnimation =
            new DoubleAnimation(oldHeight, targetHeight, new Duration(TimeSpan.FromSeconds(0.45)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

        heightAnimation.Completed += (_, _) =>
        {
            if (expand)
            {
                wrapper.Height = double.NaN;
            }
            else
            {
                wrapper.Visibility = Visibility.Collapsed;
                wrapper.Height = double.NaN;
            }
        };

        wrapper.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);

        var opacityAnimation =
            new DoubleAnimation(
                expand ? 0 : 1,
                expand ? 1 : 0,
                new Duration(TimeSpan.FromSeconds(0.45)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

        wrapper.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
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
            _projectInfoService.SaveProject(_project);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ex.ToString(), "Project Info - save error");
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

        if (!_sectionExpanded.TryGetValue(section, out var expanded) || !expanded)
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
