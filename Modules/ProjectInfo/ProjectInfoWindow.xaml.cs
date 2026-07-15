using KubaToolKit.Modules.ProjectInfo.Models;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

    public ProjectInfoWindow(
        string profileName)
    {
        InitializeComponent();

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

        foreach (var section in _project.Sections)
        {
            SectionsPanel.Children.Add(BuildSectionCard(section));
        }
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

            // Une nouvelle colonne change la structure de la table : cette
            // carte précise doit être reconstruite, mais seulement elle --
            // pas les autres sections, dont les DataGrid n'ont aucune
            // raison d'être touchés pour ce changement.
            TryCommitEdit(grid);

            var index = SectionsPanel.Children.IndexOf(card);
            var replacement = BuildSectionCard(section);

            if (index >= 0)
            {
                SectionsPanel.Children[index] = replacement;
            }

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

            // Valider une éventuelle édition en cours avant de détacher le
            // DataGrid du visuel : WPF peut se plaindre si on le retire du
            // panneau pendant qu'il essaie encore de valider une cellule
            // ou la ligne d'ajout.
            TryCommitEdit(grid);

            _project.Sections.Remove(section);
            SectionsPanel.Children.Remove(card);

            Save();
        };
        Grid.SetColumn(deleteSectionButton, 3);

        header.Children.Add(nameText);
        header.Children.Add(newColumnTextBox);
        header.Children.Add(addColumnButton);
        header.Children.Add(deleteSectionButton);

        outer.Children.Add(header);

        var table = BuildDataTable(section);

        grid = new DataGrid
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

        return card;
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
