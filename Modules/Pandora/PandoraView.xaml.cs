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
    private PandoraLoginWindow? _openLoginWindow;

    public PandoraView()
    {
        InitializeComponent();
    }

    public bool IsLoading => _loadCancellation != null;

    public void
    CancelLoad()
    {
        _loadCancellation?.Cancel();

        // ShowDialog() bloque le thread UI tant que la fenêtre de
        // connexion est ouverte : annuler le token ne suffit pas à
        // l'interrompre, il faut la fermer explicitement. Elle renvoie
        // alors DialogResult != true, ce que ShowLoginWindow traite comme
        // un échec normal.
        _openLoginWindow?.Close();
    }

    public async Task
    LoadTreeAsync(
        PandoraProfile? profile,
        string filterText)
    {
        // Jamais deux chargements/connexions en même temps : changer de
        // profil doit d'abord couper la tentative précédente, où qu'elle
        // en soit (appels réseau ou fenêtre de connexion ouverte).
        CancelLoad();

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
                await LoadTreeWithLoginRetryAsync(profile, _loadCancellation.Token);

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

    /// Comme AwsSsoService côté AWS : on tente l'appel, et seulement s'il
    /// échoue faute de session valide on ouvre la fenêtre de connexion et
    /// on réessaie une fois -- pas de vérification préalable coûteuse à
    /// chaque recherche.
    private async Task<List<PandoraGroupNode>>
    LoadTreeWithLoginRetryAsync(
        PandoraProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _pandoraService.GetTreeAsync(profile, cancellationToken);
        }
        catch (PandoraAuthRequiredException)
        {
            ProgressTextBlock.Text = $"Login required for {profile.Name}...";

            if (!ShowLoginWindow(profile))
            {
                // La fenêtre a pu se fermer sans succès pour deux raisons
                // différentes : l'utilisateur l'a fermée/annulée
                // volontairement, ou CancelLoad() l'a fermée parce qu'un
                // autre profil a été sélectionné entre-temps -- dans ce
                // second cas le token est déjà annulé, à traiter comme une
                // annulation silencieuse plutôt qu'une erreur bruyante.
                cancellationToken.ThrowIfCancellationRequested();

                throw new Exception($"Connexion à {profile.Name} annulée ou échouée.");
            }

            return await _pandoraService.GetTreeAsync(profile, cancellationToken);
        }
    }

    private bool
    ShowLoginWindow(
        PandoraProfile profile)
    {
        var owner = Window.GetWindow(this);

        var login = new PandoraLoginWindow(profile.Url)
        {
            Owner = owner
        };

        _openLoginWindow = login;

        try
        {
            var success = login.ShowDialog() == true;

            if (success)
            {
                _pandoraService.SetSession(profile.Url, login.Cookies);
            }

            return success;
        }
        finally
        {
            _openLoginWindow = null;

            // WebView2 peut laisser le focus dans un état bizarre au
            // moment où sa fenêtre hôte se ferme : on ramène explicitement
            // KubaToolKit au premier plan plutôt que d'espérer que WPF le
            // fasse tout seul.
            owner?.Activate();
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
