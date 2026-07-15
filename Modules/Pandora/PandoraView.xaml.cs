using KubaToolKit.Modules.Pandora.Models;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.Pandora;

public partial class PandoraView
    : UserControl
{
    private readonly PandoraService _pandoraService = new();
    private List<PandoraGroupNode> _allGroups = new();
    private CancellationTokenSource? _loadCancellation;

    public PandoraView()
    {
        InitializeComponent();
    }

    public bool IsLoading => _loadCancellation != null;

    public void
    CancelLoad()
    {
        _loadCancellation?.Cancel();
    }

    public async Task
    LoadTreeAsync(
        PandoraProfile? profile,
        string filterText)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            GroupsItemsControl.ItemsSource = null;
            LoadingProgressBar.IsIndeterminate = true;
            ProgressTextBlock.Text = $"Loading {profile.Name}...";

            _loadCancellation = new CancellationTokenSource();

            _allGroups =
                await _pandoraService.GetTreeAsync(profile, _loadCancellation.Token);

            ApplyFilter(filterText);
        }
        catch (OperationCanceledException)
        {
            ProgressTextBlock.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            ProgressTextBlock.Text = "Error";
            MessageBox.Show(ex.ToString(), "Pandora error");
        }
        finally
        {
            LoadingProgressBar.IsIndeterminate = false;
            _loadCancellation = null;
        }
    }

    public void
    ApplyFilter(
        string filterText)
    {
        var text = filterText.Trim();

        var filtered =
            _allGroups
                .Select(g =>
                    new PandoraGroupNode
                    {
                        Name = g.Name,

                        Agents =
                            new ObservableCollection<PandoraAgent>(
                                string.IsNullOrWhiteSpace(text)
                                    ? g.Agents
                                    : g.Agents.Where(a =>
                                        a.Alias.Contains(text, StringComparison.OrdinalIgnoreCase)))
                    })
                .Where(g => g.Count > 0)
                .ToList();

        GroupsItemsControl.ItemsSource = filtered;

        var totalAgents = _allGroups.Sum(g => g.Count);
        var shownAgents = filtered.Sum(g => g.Count);

        ProgressTextBlock.Text =
            string.IsNullOrWhiteSpace(text)
                ? $"{totalAgents} agent(s)"
                : $"{shownAgents} / {totalAgents} agent(s)";
    }

    private void
    AgentsDataGrid_DoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not DataGrid dataGrid)
            {
                return;
            }

            if (dataGrid.SelectedItem is not PandoraAgent agent)
            {
                return;
            }

            var json =
                JsonSerializer.Serialize(
                    new
                    {
                        agent.Id,
                        agent.Alias,
                        agent.Address,
                        agent.Comments,
                        agent.OsName,
                        agent.Status,
                        agent.StatusLabel
                    },
                    new JsonSerializerOptions { WriteIndented = true });

            var viewer = new JsonViewerWindow(json);
            viewer.Owner = Window.GetWindow(this);
            viewer.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Viewer error");
        }
    }
}
