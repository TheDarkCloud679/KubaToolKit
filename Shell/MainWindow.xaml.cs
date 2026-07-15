using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Infrastructure;
using KubaToolKit.Modules.ApiClient;
using KubaToolKit.Modules.CloudTrail;
using KubaToolKit.Modules.CloudWatchLogs;
using KubaToolKit.Modules.Dashboard;
using KubaToolKit.Modules.Pandora;
using KubaToolKit.Modules.Pandora.Models;
using KubaToolKit.Modules.S3Explorer;
using KubaToolKit.Modules.Sqs;
using KubaToolKit.Modules.StepFunctions;
using KubaToolKit.Shared.Behaviors;
using KubaToolKit.Shared.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KubaToolKit.Shell;

public partial class MainWindow
    : Window
{
    // New bricks are registered in ToolModuleRegistry; add typed accessors below
    // only if the Shell needs to talk to that module's specific API.
    private readonly IReadOnlyList<IToolModule> _modules = ToolModuleRegistry.CreateModules();
    private readonly DashboardView _dashboardView;
    private readonly CloudWatchLogsView _cloudWatchView;
    private readonly CloudTrailView _cloudTrailView;
    private readonly PandoraView _pandoraView;
    private readonly S3ExplorerView _s3View;
    private readonly SqsView _sqsView;
    private readonly StepFunctionsView _stepFunctionsView;
    private readonly ApiClientView _apiClientView;

    private readonly PandoraProfileService _pandoraProfileService = new();
    private List<PandoraProfile> _pandoraProfiles = new();

    private bool _windowLoaded = false;
    private bool _waitingForEndDate = false;
    private bool _forceKeepCalendarOpen = false;
    private DateTime? _startRangeDate;
    private bool _updatingDate = false;
    private DatePicker? _activeDatePicker;

    // Pixels à défiler pour un "cran" standard de molette (delta = 120).
    // La vitesse réelle appliquée est proportionnelle à e.Delta, pas un
    // saut fixe par évènement : indispensable pour les souris/trackpads à
    // défilement fluide qui envoient beaucoup de petits deltas par
    // mouvement (sinon chacun d'eux sautait du même montant fixe, ce qui
    // donnait une impression de scroll bien trop rapide).
    private const double PixelsPerNotch = 18;

    public MainWindow()
    {
        Logger.Info("MainWindow: InitializeComponent.");

        InitializeComponent();

        // Lu depuis <Version> du csproj (KubaToolKit.csproj) plutôt que
        // codé en dur ici : un seul endroit à modifier pour publier une
        // nouvelle version.
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        VersionTextBlock.Text =
            version != null
                ? $"v{version.Major}.{version.Minor}.{version.Build}"
                : "";

        _dashboardView = _modules.OfType<DashboardModule>().Single().TypedView;
        _cloudWatchView = _modules.OfType<CloudWatchLogsModule>().Single().TypedView;
        _cloudTrailView = _modules.OfType<CloudTrailModule>().Single().TypedView;
        _pandoraView = _modules.OfType<PandoraModule>().Single().TypedView;
        _s3View = _modules.OfType<S3ExplorerModule>().Single().TypedView;
        _sqsView = _modules.OfType<SqsModule>().Single().TypedView;
        _stepFunctionsView = _modules.OfType<StepFunctionsModule>().Single().TypedView;
        _apiClientView = _modules.OfType<ApiClientModule>().Single().TypedView;

        Logger.Debug($"MainWindow: {_modules.Count} module(s) instancié(s).");

        foreach (var module in _modules)
        {
            module.View.Visibility = Visibility.Collapsed;
            ModuleHost.Children.Add(module.View);
        }

        _dashboardView.Visibility = Visibility.Visible;

        Logger.Info("MainWindow: constructeur terminé.");

        _cloudWatchView.GetDateRange =
            () => (StartDatePicker.SelectedDate, StartTimeTextBox.Text, EndDatePicker.SelectedDate, EndTimeTextBox.Text);

        Loaded += MainWindow_Loaded;
        PreviewMouseWheel += MainWindow_PreviewMouseWheel;
        Closing += MainWindow_Closing;
    }

    private void
MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            _cloudWatchView.CancelSearch();
            _cloudTrailView.CancelSearch();
            _pandoraView.CancelLoad();
            _s3View.CancelSearch();
        }
        catch { }
    }

    private async void
MainWindow_Loaded(
    object sender,
    RoutedEventArgs e)
    {
        try
        {
            LoadProfiles();

            var profile =
    ProfileCombo.SelectedItem?.ToString();

            if (!string.IsNullOrWhiteSpace(profile))
            {
                await AwsSsoService
                    .EnsureLoggedIn(profile);
            }

            LoadPatterns();
            LoadCloudTrailAttributes();
            LoadPandoraProfiles();
            InitializeDates();

            _windowLoaded =
                true;

            ModeRadio_Checked(
                this,
                new RoutedEventArgs());

            Logger.Info("MainWindow: chargement initial terminé.");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow: échec du chargement initial.", ex);

            MessageBox.Show(
                ex.ToString(),
                "Startup error");
        }
    }

    private void LoadProfiles()
    {
        var chain = new CredentialProfileStoreChain();
        var profiles =
            chain.ListProfiles()
                .Select(x => x.Name)
                .Where(x => x != "default")
                .OrderBy(x => x)
                .ToList();
        ProfileCombo.ItemsSource = profiles;
        if (profiles.Any())
        {
            ProfileCombo.SelectedIndex = 0;
        }
    }

    private void LoadPatterns()
    {
        PatternCombo.ItemsSource = new List<string>
            {
                "",
                "4XX",
                "5XX",
                "Status Code",
                "ERROR",
                "TimeOut"
            };
        PatternCombo.SelectedIndex = 0;
    }

    private void LoadCloudTrailAttributes()
    {
        CloudTrailAttributeCombo.ItemsSource = CloudTrailAttributeOption.All;
        CloudTrailAttributeCombo.SelectedIndex = 0;
    }

    private void LoadPandoraProfiles()
    {
        _pandoraProfiles = _pandoraProfileService.LoadProfiles();

        PandoraProfileCombo.ItemsSource = _pandoraProfiles;

        if (_pandoraProfiles.Any())
        {
            PandoraProfileCombo.SelectedIndex = 0;
        }
    }

    private void
InitializeDates()
    {
        var today = DateTime.Today;
        StartDatePicker.SelectedDate = today;
        EndDatePicker.SelectedDate = today.AddDays(1);
        StartTimeTextBox.Text = "00:00";
        EndTimeTextBox.Text = "00:00";
    }

    private void
SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        SearchButton_Click(SearchButton, new RoutedEventArgs());
        e.Handled = true;
    }

    private void PatternCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = PatternCombo.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SearchTextBox.Text = selected;
        }

        _cloudWatchView.OnSearchTextChanged(SearchTextBox.Text);
    }

    private async void
    ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowLoaded)
        {
            return;
        }

        var profile =
            ProfileCombo.SelectedItem?.ToString();

        if (S3ModeRadio?.IsChecked == true)
        {
            await _s3View.OnProfileChanged(profile);
        }
        else if (SqsModeRadio?.IsChecked == true)
        {
            await _sqsView.OnProfileChanged(profile);
        }
        else if (DashboardModeRadio?.IsChecked == true)
        {
            await _dashboardView.OnProfileChanged(profile);
        }
        else if (StepFunctionsModeRadio?.IsChecked == true)
        {
            await _stepFunctionsView.OnProfileChanged(profile);
        }
        else if (ApiClientModeRadio?.IsChecked == true)
        {
            // Aucun profil AWS nécessaire : l'API Client est indépendant
            // du système de credentials/profils de l'app.
        }
        else if (CloudWatchModeRadio?.IsChecked == true)
        {
            await LoadCloudWatchLogGroupsAsync(profile);
        }
        else
        {
            // CloudTrail interroge à la demande (bouton Search) : rien à
            // précharger au changement de profil, contrairement à
            // CloudWatch qui a besoin de la liste des log groups.
        }
    }

    private async void
    PandoraProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowLoaded || PandoraModeRadio?.IsChecked != true)
        {
            return;
        }

        await _pandoraView.LoadTreeAsync(
            PandoraProfileCombo.SelectedItem as PandoraProfile,
            SearchTextBox.Text);
    }

    private async Task
    LoadCloudWatchLogGroupsAsync(string? profile)
    {
        SearchButton.IsEnabled = false;
        SearchButton.Content = "Loading log groups...";

        try
        {
            await _cloudWatchView.LoadLogGroupsAsync(profile);
        }
        finally
        {
            SearchButton.IsEnabled = true;
            SearchButton.Content = "Search";
        }
    }

    private async void
        SearchButton_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (_cloudWatchView.IsSearchRunning)
        {
            _cloudWatchView.CancelSearch();
            return;
        }

        if (_cloudTrailView.IsSearchRunning)
        {
            _cloudTrailView.CancelSearch();
            return;
        }

        try
        {
            if (S3ModeRadio?.IsChecked == true)
            {
                SearchButton.IsEnabled = false;

                try
                {
                    await _s3View.RunSearchAsync(SearchTextBox.Text);
                }
                finally
                {
                    SearchButton.IsEnabled = true;
                }

                return;
            }

            if (SqsModeRadio?.IsChecked == true)
            {
                SearchButton.IsEnabled = false;

                try
                {
                    await _sqsView.RefreshAsync();
                }
                finally
                {
                    SearchButton.IsEnabled = true;
                }

                return;
            }

            if (DashboardModeRadio?.IsChecked == true)
            {
                SearchButton.IsEnabled = false;

                try
                {
                    await _dashboardView.RefreshAsync();
                }
                finally
                {
                    SearchButton.IsEnabled = true;
                }

                return;
            }

            if (StepFunctionsModeRadio?.IsChecked == true)
            {
                SearchButton.IsEnabled = false;

                try
                {
                    await _stepFunctionsView.RefreshAsync();
                }
                finally
                {
                    SearchButton.IsEnabled = true;
                }

                return;
            }

            if (ApiClientModeRadio?.IsChecked == true)
            {
                SearchButton.IsEnabled = false;

                try
                {
                    await _apiClientView.SendAsync();
                }
                finally
                {
                    SearchButton.IsEnabled = true;
                }

                return;
            }

            if (PandoraModeRadio?.IsChecked == true)
            {
                // Pas d'appel réseau ici : l'arbre est déjà chargé (au
                // changement de profil), Search ne fait que filtrer ce qui
                // est affiché par nom d'agent.
                _pandoraView.ApplyFilter(SearchTextBox.Text);

                return;
            }

            if (CloudTrailModeRadio?.IsChecked == true)
            {
                if (!TryGetValidatedTimeRange(out var ctProfile))
                {
                    return;
                }

                var attribute =
                    CloudTrailAttributeCombo.SelectedItem as CloudTrailAttributeOption
                    ?? CloudTrailAttributeOption.All[0];

                SearchButton.Content = "Cancel";

                try
                {
                    await _cloudTrailView.RunSearchAsync(
                        ctProfile,
                        attribute.Key,
                        SearchTextBox.Text,
                        StartDatePicker.SelectedDate,
                        StartTimeTextBox.Text,
                        EndDatePicker.SelectedDate,
                        EndTimeTextBox.Text);
                }
                finally
                {
                    SearchButton.Content = "Search";
                }

                return;
            }

            if (!TryGetValidatedTimeRange(out var profile))
            {
                return;
            }

            SearchButton.Content = "Cancel";

            try
            {
                await _cloudWatchView.RunSearchAsync(
                    profile,
                    SearchTextBox.Text,
                    StartDatePicker.SelectedDate,
                    StartTimeTextBox.Text,
                    EndDatePicker.SelectedDate,
                    EndTimeTextBox.Text);
            }
            finally
            {
                SearchButton.Content = "Search";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Search error");
        }
    }

    /// Valide le profil AWS et la plage date/heure partagée (Start/End)
    /// avant une recherche CloudWatch ou CloudTrail -- les deux modes
    /// utilisent exactement les mêmes champs du Shell et les mêmes règles.
    private bool
    TryGetValidatedTimeRange(
        out string profile)
    {
        profile =
            ProfileCombo.SelectedItem?.ToString()
            ?? "";

        if (string.IsNullOrWhiteSpace(profile))
        {
            MessageBox.Show(
                "Choisir un profil AWS");

            return false;
        }

        if (!TimeSpan.TryParse(
                StartTimeTextBox.Text,
                out var startTime))
        {
            MessageBox.Show(
                "Invalid start time.\nFormat attendu : HH:mm",
                "Time error");

            return false;
        }

        if (!TimeSpan.TryParse(
                EndTimeTextBox.Text,
                out var endTime))
        {
            MessageBox.Show(
                "Invalid end time.\nFormat attendu : HH:mm",
                "Time error");

            return false;
        }

        if (startTime.TotalHours >= 24
            || endTime.TotalHours >= 24)
        {
            MessageBox.Show(
                "Hour must be between 00:00 and 23:59.",
                "Time error");

            return false;
        }

        if (StartDatePicker.SelectedDate == null
            || EndDatePicker.SelectedDate == null)
        {
            MessageBox.Show(
                "Please select dates.",
                "Date error");

            return false;
        }

        var startDateTime =
            StartDatePicker.SelectedDate.Value.Date + startTime;

        var endDateTime =
            EndDatePicker.SelectedDate.Value.Date + endTime;

        if (endDateTime <= startDateTime)
        {
            MessageBox.Show(
                "End date/time must be after start date/time.",
                "Date error");

            return false;
        }

        return true;
    }

    private void
    DatePicker_CalendarOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker picker)
        {
            return;
        }

        // Que ce soit Start ou End qu'on ouvre en premier, le même flux
        // "1er clic = début, 2e clic = fin" s'applique.
        _activeDatePicker = picker;
        picker.SelectedDateChanged -= StartDatePicker_SelectedDateChanged;
        picker.SelectedDateChanged += StartDatePicker_SelectedDateChanged;

        // Empêcher la fermeture automatique
        picker.CalendarClosed -= StartDatePicker_CalendarClosed;
        picker.CalendarClosed += StartDatePicker_CalendarClosed;
    }

    private void
    StartDatePicker_CalendarClosed(object? sender, RoutedEventArgs e)
    {
        var picker = sender as DatePicker ?? _activeDatePicker;

        if (picker == null)
        {
            return;
        }

        // on attend un 2e clic
        if (_waitingForEndDate && _forceKeepCalendarOpen)
        {
            // Le 2e clic fixe toujours la date de fin : le calendrier doit
            // se rouvrir sous le champ End, même si le 1er clic a eu lieu
            // sur Start.
            var targetPicker = EndDatePicker;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                new Action(() =>
                {
                    // On change de champ (le 1er clic avait ouvert Start) :
                    // on ouvre directement le calendrier de fin.
                    if (!ReferenceEquals(picker, targetPicker))
                    {
                        targetPicker.IsDropDownOpen = true;

                        return;
                    }

                    // Sinon (le 1er clic avait déjà ouvert End) : on garde
                    // le comportement d'origine, basé sur le focus.
                    if (picker.IsKeyboardFocusWithin)
                    {
                        targetPicker.IsDropDownOpen = true;
                    }
                    else
                    {
                        // utilisateur a cliqué ailleurs
                        // garder start mémorisé
                        // mais arrêter la réouverture
                        _forceKeepCalendarOpen = false;
                    }
                }));
        }
    }

    private void
StartDatePicker_SelectedDateChanged(
    object? sender,
    SelectionChangedEventArgs e)
    {
        if (_updatingDate)
        {
            return;
        }

        if (sender
            is not DatePicker picker)
        {
            return;
        }

        if (picker.SelectedDate
            == null)
        {
            return;
        }

        _updatingDate =
            true;

        try
        {
            var selectedDate =
                picker.SelectedDate
                    .Value
                    .Date;

            // PREMIER CLIC
            if (!_waitingForEndDate)
            {
                _startRangeDate =
                    selectedDate;

                StartDatePicker.SelectedDate =
                    selectedDate;

                EndDatePicker.SelectedDate =
                    selectedDate.AddDays(1);

                StartTimeTextBox.Text =
                    "00:00";

                EndTimeTextBox.Text =
                    "00:00";

                _waitingForEndDate =
                    true;

                _forceKeepCalendarOpen =
                    true;

                return;
            }

            // SECOND CLIC
            if (_startRangeDate
                != null)
            {
                StartDatePicker.SelectedDate =
                    _startRangeDate.Value;

                EndDatePicker.SelectedDate =
                    selectedDate;
            }

            // sécurité
            if (EndDatePicker.SelectedDate
                <
                StartDatePicker.SelectedDate)
            {
                var temp =
                    StartDatePicker.SelectedDate;

                StartDatePicker.SelectedDate =
                    EndDatePicker.SelectedDate;

                EndDatePicker.SelectedDate =
                    temp;
            }

            _waitingForEndDate =
                false;

            _forceKeepCalendarOpen =
                false;

            _startRangeDate =
                null;

            picker
                .IsDropDownOpen =
                    false;
        }
        finally
        {
            _updatingDate =
                false;
        }
    }

    private async void
  ModeRadio_Checked(
      object sender,
      RoutedEventArgs e)
    {
        // éviter exécution pendant InitializeComponent
        if (!_windowLoaded)
        {
            return;
        }

        bool isS3 =
            S3ModeRadio
                ?.IsChecked
            == true;

        bool isSqs =
            SqsModeRadio
                ?.IsChecked
            == true;

        bool isDashboard =
            DashboardModeRadio
                ?.IsChecked
            == true;

        bool isStepFunctions =
            StepFunctionsModeRadio
                ?.IsChecked
            == true;

        bool isApiClient =
            ApiClientModeRadio
                ?.IsChecked
            == true;

        bool isCloudTrail =
            CloudTrailModeRadio
                ?.IsChecked
            == true;

        bool isPandora =
            PandoraModeRadio
                ?.IsChecked
            == true;

        bool isCloudWatch =
            !isS3 && !isSqs && !isDashboard && !isStepFunctions && !isApiClient && !isCloudTrail && !isPandora;

        _dashboardView.Visibility =
            isDashboard ? Visibility.Visible : Visibility.Collapsed;

        _cloudWatchView.Visibility =
            isCloudWatch ? Visibility.Visible : Visibility.Collapsed;

        _cloudTrailView.Visibility =
            isCloudTrail ? Visibility.Visible : Visibility.Collapsed;

        _pandoraView.Visibility =
            isPandora ? Visibility.Visible : Visibility.Collapsed;

        _s3View.Visibility =
            isS3 ? Visibility.Visible : Visibility.Collapsed;

        _sqsView.Visibility =
            isSqs ? Visibility.Visible : Visibility.Collapsed;

        _stepFunctionsView.Visibility =
            isStepFunctions ? Visibility.Visible : Visibility.Collapsed;

        _apiClientView.Visibility =
            isApiClient ? Visibility.Visible : Visibility.Collapsed;

        ProfilePatternSearchRow.Visibility =
            isApiClient ? Visibility.Collapsed : Visibility.Visible;

        // Profile AWS et Profile Pandora partagent la même colonne : deux
        // mondes d'identifiants totalement distincts, jamais utiles en
        // même temps.
        ProfileGroup.Visibility =
            isPandora ? Visibility.Collapsed : Visibility.Visible;

        PandoraProfileGroup.Visibility =
            isPandora ? Visibility.Visible : Visibility.Collapsed;

        // Dates/heures servent au filtrage CloudWatch et CloudTrail (tous
        // deux interrogent une plage temporelle) ; Pandora n'affiche que
        // l'état courant, pas d'historique.
        DateRangeRow.Visibility =
            isCloudWatch || isCloudTrail ? Visibility.Visible : Visibility.Collapsed;

        // Pattern ne sert qu'au filtrage CloudWatch ; Attribute ne sert
        // qu'à CloudTrail (les deux partagent la même colonne, jamais
        // visibles ensemble). Search sert aussi en S3 (recherche dans les
        // dossiers) et en Pandora (filtre l'arbre déjà chargé par nom
        // d'agent). Dashboard/SQS/Step Functions ont chacun leur propre
        // bouton "Refresh" et n'utilisent ni le texte de recherche ni le
        // bouton d'action partagé : les deux disparaissent complètement
        // pour ces modules plutôt que d'occuper une ligne pour rien. Le
        // bouton reste toujours sur la même ligne que Search, jamais avec
        // les dates.
        PatternGroup.Visibility =
            isCloudWatch ? Visibility.Visible : Visibility.Collapsed;

        AttributeGroup.Visibility =
            isCloudTrail ? Visibility.Visible : Visibility.Collapsed;

        SearchGroup.Visibility =
            isCloudWatch || isCloudTrail || isS3 || isPandora ? Visibility.Visible : Visibility.Collapsed;

        DateFieldsGroup.Visibility =
            isCloudWatch || isCloudTrail ? Visibility.Visible : Visibility.Collapsed;

        SearchButton.Visibility =
            isCloudWatch || isCloudTrail || isS3 || isPandora ? Visibility.Visible : Visibility.Collapsed;

        if (isS3)
        {
            await _s3View.OnProfileChanged(
                ProfileCombo.SelectedItem?.ToString());
        }
        else if (isSqs)
        {
            await _sqsView.OnProfileChanged(
                ProfileCombo.SelectedItem?.ToString());
        }
        else if (isDashboard)
        {
            await _dashboardView.OnProfileChanged(
                ProfileCombo.SelectedItem?.ToString());
        }
        else if (isStepFunctions)
        {
            await _stepFunctionsView.OnProfileChanged(
                ProfileCombo.SelectedItem?.ToString());
        }
        else if (isCloudWatch)
        {
            await LoadCloudWatchLogGroupsAsync(
                ProfileCombo.SelectedItem?.ToString());
        }
        else if (isPandora)
        {
            await _pandoraView.LoadTreeAsync(
                PandoraProfileCombo.SelectedItem as PandoraProfile,
                SearchTextBox.Text);
        }

        // isCloudTrail : rien à précharger, la recherche se fait à la
        // demande (bouton Search).
    }

    private void
MainWindow_PreviewMouseWheel(
    object sender,
    MouseWheelEventArgs e)
    {
        var dependencyObject =
            e.OriginalSource
            as DependencyObject;

        ScrollViewer? currentScroll =
            null;

        while (dependencyObject != null)
        {
            if (dependencyObject
                is ScrollViewer scrollViewer)
            {
                currentScroll =
                    scrollViewer;

                break;
            }

            dependencyObject =
                VisualTreeHelper
                    .GetParent(
                        dependencyObject);
        }

        if (currentScroll == null)
        {
            return;
        }

        // Certains ScrollViewer (ex: popup de ComboBox) gèrent eux-mêmes
        // leur vitesse de défilement via ScrollSpeedBehavior ; on ne doit
        // pas leur imposer la vitesse générique par-dessus.
        if (ScrollSpeedBehavior.GetLinesPerNotch(currentScroll) > 0)
        {
            return;
        }

        bool scrollingUp =
            e.Delta > 0;

        bool atTop =
            currentScroll.VerticalOffset <= 0;

        bool atBottom =
            currentScroll.VerticalOffset >=
            currentScroll.ScrollableHeight;

        // Proportionnel à e.Delta (et non un saut fixe par évènement) :
        // un demi-cran ne doit déplacer qu'à moitié moins qu'un cran plein.
        double delta =
            -e.Delta / 120.0
            * PixelsPerNotch;

        // Scroll interne normal
        if ((!scrollingUp && !atBottom)
            ||
            (scrollingUp && !atTop))
        {
            currentScroll
                .ScrollToVerticalOffset(
                    currentScroll.VerticalOffset
                    + delta);

            e.Handled =
                true;

            return;
        }

        // Chercher un parent scrollable
        DependencyObject? parent =
            VisualTreeHelper
                .GetParent(
                    currentScroll);

        while (parent != null)
        {
            if (parent
                is ScrollViewer parentScroll)
            {
                parentScroll
                    .ScrollToVerticalOffset(
                        parentScroll.VerticalOffset
                        + delta);

                e.Handled =
                    true;

                return;
            }

            parent =
                VisualTreeHelper
                    .GetParent(
                        parent);
        }
    }
}
