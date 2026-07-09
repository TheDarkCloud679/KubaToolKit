using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Infrastructure;
using KubaToolKit.Modules.ApiClient;
using KubaToolKit.Modules.CloudWatchLogs;
using KubaToolKit.Modules.Dashboard;
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
    private readonly S3ExplorerView _s3View;
    private readonly SqsView _sqsView;
    private readonly StepFunctionsView _stepFunctionsView;
    private readonly ApiClientView _apiClientView;

    private bool _windowLoaded = false;
    private bool _editingSecondHourDigit = false;
    private bool _editingSecondMinuteDigit = false;
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
    private const double PixelsPerNotch = 9;

    public MainWindow()
    {
        Logger.Info("MainWindow: InitializeComponent.");

        InitializeComponent();

        _dashboardView = _modules.OfType<DashboardModule>().Single().TypedView;
        _cloudWatchView = _modules.OfType<CloudWatchLogsModule>().Single().TypedView;
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
        else
        {
            await LoadCloudWatchLogGroupsAsync(profile);
        }
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

            var profile =
    ProfileCombo
        .SelectedItem?
        .ToString();

            if (string.IsNullOrWhiteSpace(
                    profile))
            {
                MessageBox.Show(
                    "Choisir un profil AWS");

                return;
            }

            // Vérification format heure
            if (!TimeSpan.TryParse(
                    StartTimeTextBox.Text,
                    out var startTime))
            {
                MessageBox.Show(
                    "Invalid start time.\nFormat attendu : HH:mm",
                    "Time error");

                return;
            }

            if (!TimeSpan.TryParse(
                    EndTimeTextBox.Text,
                    out var endTime))
            {
                MessageBox.Show(
                    "Invalid end time.\nFormat attendu : HH:mm",
                    "Time error");

                return;
            }

            // plage valide
            if (startTime.TotalHours
                >= 24
                ||
                endTime.TotalHours
                >= 24)
            {
                MessageBox.Show(
                    "Hour must be between 00:00 and 23:59.",
                    "Time error");

                return;
            }

            if (StartDatePicker.SelectedDate
                == null
                ||
                EndDatePicker.SelectedDate
                == null)
            {
                MessageBox.Show(
                    "Please select dates.",
                    "Date error");

                return;
            }

            var startDateTime =
                StartDatePicker
                    .SelectedDate
                    .Value
                    .Date
                    + startTime;

            var endDateTime =
                EndDatePicker
                    .SelectedDate
                    .Value
                    .Date
                    + endTime;

            // ordre chrono
            if (endDateTime
                <= startDateTime)
            {
                MessageBox.Show(
                    "End date/time must be after start date/time.",
                    "Date error");

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

    private void
    TimeTextBox_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender
            is not TextBox textBox)
        {
            return;
        }

        e.Handled =
            true;

        // Index du caractère cliqué, indépendant du focus actuel : permet
        // de savoir si l'utilisateur vise la partie heure ou minute même
        // au tout premier clic (avant que le focus ne soit posé).
        int charIndex =
            textBox.GetCharacterIndexFromPoint(
                e.GetPosition(textBox),
                true);

        if (!textBox.IsKeyboardFocusWithin)
        {
            textBox.Focus();
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (charIndex <= 2)
                {
                    SelectHourPart(
                        textBox);
                }
                else
                {
                    SelectMinutePart(
                        textBox);
                }
            }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void
    SelectHourPart(
        TextBox textBox,
        bool reset = true)
    {
        if (reset)
        {
            _editingSecondHourDigit =
                false;
        }

        textBox.SelectionStart =
            0;

        textBox.SelectionLength =
            2;
    }

    private void
SelectMinutePart(
    TextBox textBox,
    bool reset = true)
    {
        if (reset)
        {
            _editingSecondMinuteDigit =
                false;
        }

        textBox.SelectionStart =
            3;

        textBox.SelectionLength =
                2;
    }

    private void
    TimeTextBox_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender
            is not TextBox textBox)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                SelectHourPart(
                    textBox);
            }),
            System.Windows.Threading.DispatcherPriority.Input);
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

    private void
TimeTextBox_LostFocus(
    object sender,
    RoutedEventArgs e)
    {
        FormatTimeTextBox(
            sender as TextBox);
    }

    private void
TimeTextBox_PreviewKeyDown(
    object sender,
    KeyEventArgs e)
    {
        if (sender
            is not TextBox textBox)
        {
            return;
        }

        string[] parts =
            textBox.Text
                .Split(':');

        // sécurité
        if (parts.Length != 2)
        {
            textBox.Text =
                "00:00";

            parts =
                textBox.Text.Split(':');
        }

        // navigation gauche/droite
        if (e.Key == Key.Left)
        {
            SelectHourPart(
                textBox);

            e.Handled =
                true;

            return;
        }

        if (e.Key == Key.Right
            ||
            e.Key == Key.Tab)
        {
            SelectMinutePart(
                textBox);

            e.Handled =
                true;

            return;
        }

        // flèches haut/bas
        if (e.Key == Key.Up
            ||
            e.Key == Key.Down)
        {
            bool increase =
                e.Key == Key.Up;

            bool editingHours =
                textBox.SelectionStart < 3;

            if (editingHours)
            {
                int hours =
                    int.Parse(parts[0]);

                hours +=
                    increase ? 1 : -1;

                if (hours > 23)
                {
                    hours =
                        0;
                }

                if (hours < 0)
                {
                    hours =
                        23;
                }

                textBox.Text =
                    $"{hours:00}:{parts[1]}";

                SelectHourPart(
                    textBox);
            }
            else
            {
                int minutes =
                    int.Parse(parts[1]);

                minutes +=
                    increase ? 1 : -1;

                if (minutes > 59)
                {
                    minutes =
                        0;
                }

                if (minutes < 0)
                {
                    minutes =
                        59;
                }

                textBox.Text =
                    $"{parts[0]}:{minutes:00}";

                SelectMinutePart(
                    textBox);
            }

            e.Handled =
                true;

            return;
        }

        // chiffres seulement
        bool isDigit =
            (e.Key >= Key.D0
             && e.Key <= Key.D9)
            ||
            (e.Key >= Key.NumPad0
             && e.Key <= Key.NumPad9);

        if (!isDigit)
        {
            return;
        }

        string digit =
            e.Key >= Key.NumPad0
                ? ((int)e.Key
                    - (int)Key.NumPad0)
                    .ToString()
                : ((int)e.Key
                    - (int)Key.D0)
                    .ToString();

        bool editingHour =
            textBox.SelectionStart < 3;

        // édition HH
        if (editingHour)
        {
            if (!_editingSecondHourDigit)
            {
                parts[0] =
                    $"{digit}{parts[0][1]}";

                _editingSecondHourDigit =
                    true;

                textBox.Text =
                    $"{parts[0]}:{parts[1]}";

                SelectHourPart(
                    textBox,
                    false);
            }
            else
            {
                parts[0] =
                    $"{parts[0][0]}{digit}";

                int hours =
                    Math.Clamp(
                        int.Parse(parts[0]),
                        0,
                        23);

                textBox.Text =
                    $"{hours:00}:{parts[1]}";

                _editingSecondHourDigit =
                    false;

                SelectMinutePart(
                    textBox);
            }
        }
        else
        {
            if (!_editingSecondMinuteDigit)
            {
                parts[1] =
                    $"{digit}{parts[1][1]}";

                _editingSecondMinuteDigit =
                    true;

                textBox.Text =
                    $"{parts[0]}:{parts[1]}";

                SelectMinutePart(
                    textBox,
                    false);
            }
            else
            {
                parts[1] =
                    $"{parts[1][0]}{digit}";

                int minutes =
                    Math.Clamp(
                        int.Parse(parts[1]),
                        0,
                        59);

                textBox.Text =
                    $"{parts[0]}:{minutes:00}";

                _editingSecondMinuteDigit =
                    false;

                SelectHourPart(
                    textBox);
            }
        }
        e.Handled =
            true;
    }

    private void
FormatTimeTextBox(
    TextBox? textBox)
    {
        if (textBox == null)
        {
            return;
        }

        string[] parts =
            textBox.Text
                .Split(':');

        if (parts.Length != 2)
        {
            textBox.Text =
                "00:00";

            return;
        }

        int hours =
            Math.Clamp(
                int.TryParse(
                    parts[0],
                    out var h)
                ? h
                : 0,
                0,
                23);

        int minutes =
            Math.Clamp(
                int.TryParse(
                    parts[1],
                    out var m)
                ? m
                : 0,
                0,
                59);

        textBox.Text =
            $"{hours:00}:{minutes:00}";
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

        bool isCloudWatch =
            !isS3 && !isSqs && !isDashboard && !isStepFunctions && !isApiClient;

        _dashboardView.Visibility =
            isDashboard ? Visibility.Visible : Visibility.Collapsed;

        _cloudWatchView.Visibility =
            isCloudWatch ? Visibility.Visible : Visibility.Collapsed;

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

        // Dates/heures ne servent qu'au filtrage CloudWatch.
        DateRangeRow.Visibility =
            isCloudWatch ? Visibility.Visible : Visibility.Collapsed;

        // Pattern ne sert qu'au filtrage CloudWatch ; Search sert aussi en
        // S3 (recherche dans les dossiers). Dashboard/SQS/Step Functions
        // ont chacun leur propre bouton "Refresh" et n'utilisent ni le
        // texte de recherche ni le bouton d'action partagé : les deux
        // disparaissent complètement pour ces modules plutôt que
        // d'occuper une ligne pour rien. Le bouton reste toujours sur la
        // même ligne que Search, jamais avec les dates.
        PatternGroup.Visibility =
            isCloudWatch ? Visibility.Visible : Visibility.Collapsed;

        SearchGroup.Visibility =
            isCloudWatch || isS3 ? Visibility.Visible : Visibility.Collapsed;

        DateFieldsGroup.Visibility =
            isCloudWatch ? Visibility.Visible : Visibility.Collapsed;

        SearchButton.Visibility =
            isCloudWatch || isS3 ? Visibility.Visible : Visibility.Collapsed;

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
