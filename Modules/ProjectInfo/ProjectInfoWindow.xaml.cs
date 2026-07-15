using KubaToolKit.Modules.ProjectInfo.Models;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KubaToolKit.Modules.ProjectInfo;

/// Glossaire libre par projet (profil AWS) : sections définies par
/// l'utilisateur (Contacts, Équipements réseau, VPN, ou tout autre nom),
/// chacune avec ses propres colonnes. Enregistré automatiquement à chaque
/// modification dans Config/project-info.json, partagé entre collègues
/// via ce même fichier (copie manuelle, lecteur réseau, ou suivi dans git
/// selon ce que l'équipe préfère).
public partial class ProjectInfoWindow
    : Window
{
    private readonly ProjectInfoService _projectInfoService = new();
    private readonly ProjectInfoRoot _root;
    private readonly ProjectInfoProject _project;

    public ProjectInfoWindow(
        string profileName)
    {
        InitializeComponent();

        Title = $"Project Info - {profileName}";
        TitleTextBlock.Text = $"Project Info - {profileName}";

        _root = _projectInfoService.Load();
        _project = _projectInfoService.GetOrCreateProject(_root, profileName);

        SectionPresetCombo.ItemsSource = ProjectInfoService.SectionPresets.Keys.ToList();
        SectionPresetCombo.SelectedIndex = 0;

        RenderSections();
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

        _project.Sections.Add(
            new ProjectInfoSection
            {
                Name = name,
                Columns = columns
            });

        NewSectionNameTextBox.Text = "";

        RenderSections();
        Save();
    }

    private void
    RenderSections()
    {
        SectionsPanel.Children.Clear();

        foreach (var section in _project.Sections)
        {
            SectionsPanel.Children.Add(BuildSectionCard(section));
        }
    }

    private UIElement
    BuildSectionCard(
        ProjectInfoSection section)
    {
        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)FindResource("RadiusMedium"),
            Background = (Brush)FindResource("SurfaceBrush"),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("CardShadowEffect"),
            Padding = (Thickness)FindResource("CardPadding"),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var outer = new StackPanel();

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock
        {
            Text = section.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("AccentBrush")
        };
        Grid.SetColumn(nameText, 0);

        var newColumnTextBox = new TextBox
        {
            Width = 140,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Nom de la nouvelle colonne"
        };
        Grid.SetColumn(newColumnTextBox, 1);

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

            RenderSections();
            Save();
        };
        Grid.SetColumn(addColumnButton, 2);

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

            _project.Sections.Remove(section);

            RenderSections();
            Save();
        };
        Grid.SetColumn(deleteSectionButton, 3);

        header.Children.Add(nameText);
        header.Children.Add(newColumnTextBox);
        header.Children.Add(addColumnButton);
        header.Children.Add(deleteSectionButton);

        outer.Children.Add(header);

        var table = BuildDataTable(section);

        var grid = new DataGrid
        {
            ItemsSource = table.DefaultView,
            AutoGenerateColumns = true,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 320
        };

        table.RowChanged += (_, __) => SyncAndSave(section, table);
        table.RowDeleted += (_, __) => SyncAndSave(section, table);

        outer.Children.Add(grid);
        card.Child = outer;

        return card;
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
        section.Rows =
            table.Rows
                .Cast<DataRow>()
                .Where(r => r.RowState != DataRowState.Deleted)
                .Select(r =>
                {
                    var dict = new Dictionary<string, string>();

                    foreach (var column in section.Columns)
                    {
                        dict[column] = r[column]?.ToString() ?? "";
                    }

                    return dict;
                })
                .ToList();

        table.AcceptChanges();

        Save();
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
}
