using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using KubaToolKit.Shared.Windows;

namespace KubaToolKit.Modules.AtlassianSearch;

// Same idea as ConfluencePageViewerWindow -- opening a Jira issue no
// longer has to mean leaving the app. Goes further than a read-only
// view, though: status (via the issue's actual workflow transitions,
// not a raw field write) and assignee can be changed, and comments can
// be read and added, with the internal/public distinction Service
// Management issues support.
public partial class JiraIssueViewerWindow
    : Window
{
    private const string UnassignedName = "(Unassigned)";

    // Jira's rendered ADF HTML doesn't reuse Confluence's macro class
    // names, so this is a separate, smaller approximation rather than
    // sharing ConfluencePageViewerWindow's stylesheet.
    private const string JiraCss =
        """
        body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 14px; color: #172B4D; line-height: 1.5; padding: 4px; }
        h1, h2, h3, h4, h5, h6 { font-family: 'Segoe UI', Arial, sans-serif; font-weight: 600; color: #172B4D; margin-top: 16px; margin-bottom: 6px; }
        a { color: #0052CC; text-decoration: none; }
        a:hover { text-decoration: underline; }
        img { max-width: 100%; height: auto; }
        table { border-collapse: collapse; margin: 8px 0; }
        table th, table td { border: 1px solid #DFE1E6; padding: 6px 10px; vertical-align: top; }
        table th { background-color: #F4F5F7; font-weight: 600; text-align: left; }
        code, tt { font-family: Consolas, monospace; background-color: #F4F5F7; padding: 1px 4px; border-radius: 3px; font-size: 13px; }
        pre { font-family: Consolas, monospace; background-color: #F4F5F7; border: 1px solid #DFE1E6; border-radius: 3px; padding: 10px; overflow-x: auto; font-size: 13px; white-space: pre-wrap; }
        blockquote { border-left: 3px solid #DFE1E6; margin: 10px 0; padding: 4px 12px; color: #42526E; }
        ul, ol { margin: 4px 0; padding-left: 24px; }
        """;

    private readonly AtlassianService _atlassianService;
    private readonly AtlassianSettings _settings;
    private readonly string _issueKey;
    private readonly string _fallbackUrl;
    private readonly bool _isServiceDeskIssue;

    private List<NameValue> _assignableUsers = new();
    private List<JiraTransition> _transitions = new();

    public JiraIssueViewerWindow(
        AtlassianService atlassianService,
        AtlassianSettings settings,
        string issueKey,
        string fallbackUrl,
        bool isServiceDeskIssue)
    {
        InitializeComponent();

        _atlassianService = atlassianService;
        _settings = settings;
        _issueKey = issueKey;
        _fallbackUrl = fallbackUrl;
        _isServiceDeskIssue = isServiceDeskIssue;

        KeyText.Text = issueKey;
        OpenInBrowserButton.IsEnabled = !string.IsNullOrWhiteSpace(fallbackUrl);
        VisibilityPanel.Visibility = isServiceDeskIssue ? Visibility.Visible : Visibility.Collapsed;

        AssigneeSearchBox.TextChanged += (_, __) =>
        {
            var text = AssigneeSearchBox.Text.Trim();

            AssigneeCombo.Items.Filter =
                string.IsNullOrEmpty(text)
                    ? null
                    : obj => obj is NameValue nv && nv.Display.Contains(text, StringComparison.OrdinalIgnoreCase);
        };

        Loaded += async (_, __) =>
        {
            // Actions from this window (status/assignee changes, comments)
            // happen as whichever account owns the configured API token --
            // shown once up front so that's never ambiguous mid-edit.
            var currentUser = await _atlassianService.GetCurrentJiraUserDisplayName(_settings);

            ConnectedAsText.Text = string.IsNullOrWhiteSpace(currentUser) ? "" : $"Connected as {currentUser}";

            await LoadAsync();
        };
    }

    private async Task
    LoadAsync()
    {
        try
        {
            var detail = await _atlassianService.GetJiraIssueDetail(_settings, _issueKey);

            KeyText.Text = detail.Key;
            SummaryText.Text = detail.Summary;
            PriorityBadgeText.Text = detail.Priority;
            StatusBadgeText.Text = detail.Status;

            var html =
                "<html><head><meta charset=\"utf-8\"/>"
                + $"<style>{JiraCss}</style>"
                + "</head><body>"
                + (string.IsNullOrWhiteSpace(detail.DescriptionHtml) ? "<p><em>No description.</em></p>" : detail.DescriptionHtml)
                + "</body></html>";

            DescriptionBrowser.NavigateToString(html);
            DescriptionBrowser.Visibility = Visibility.Visible;
            DescriptionStatusText.Visibility = Visibility.Collapsed;

            var transitionsTask = _atlassianService.GetJiraTransitions(_settings, detail.Key);
            var assignableUsersTask = _atlassianService.GetJiraAssignableUsers(_settings, detail.Key);
            var commentsTask = _atlassianService.GetJiraComments(_settings, detail.Key);

            await Task.WhenAll(transitionsTask, assignableUsersTask, commentsTask);

            _transitions = transitionsTask.Result;
            StatusCombo.ItemsSource = _transitions;

            if (_transitions.Count > 0)
            {
                StatusCombo.SelectedIndex = 0;
            }

            CurrentAssigneeText.Text = $"Currently: {detail.Assignee}";

            _assignableUsers = assignableUsersTask.Result;

            var assigneeOptions =
                new List<NameValue> { new("", UnassignedName) }.Concat(_assignableUsers).ToList();

            // The "assignable" search is who could be assigned right now,
            // which isn't guaranteed to include whoever already is (e.g.
            // someone no longer active on the project) -- added here so
            // the combo still shows and can keep the actual current
            // assignee instead of silently falling back to "Unassigned".
            if (!string.IsNullOrWhiteSpace(detail.AssigneeAccountId)
                && !assigneeOptions.Any(a => string.Equals(a.Value, detail.AssigneeAccountId, StringComparison.OrdinalIgnoreCase)))
            {
                assigneeOptions.Add(new NameValue(detail.AssigneeAccountId, detail.Assignee));
            }

            AssigneeCombo.ItemsSource = assigneeOptions;

            var currentAssignee =
                assigneeOptions.FirstOrDefault(a =>
                    string.Equals(a.Value, detail.AssigneeAccountId, StringComparison.OrdinalIgnoreCase));

            AssigneeCombo.SelectedItem = currentAssignee.Value != null ? currentAssignee : assigneeOptions[0];

            CommentsItemsControl.ItemsSource = commentsTask.Result;
        }
        catch (Exception ex)
        {
            Logger.Error("JiraIssueViewerWindow: failed to load issue.", ex);

            DescriptionStatusText.Text =
                string.IsNullOrWhiteSpace(_fallbackUrl)
                    ? $"Could not load this issue ({ex.Message})."
                    : $"Could not load this issue ({ex.Message}). Use \"Open in browser\" instead.";
        }
    }

    private async void
    RefreshButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await LoadAsync();

    private void
    StatusCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (StatusCombo.SelectedItem is not JiraTransition transition)
        {
            return;
        }

        RequiredFieldsItemsControl.ItemsSource = transition.RequiredFields;

        var requirements =
            new[] { transition.RequiresComment ? "a comment" : null }
                .Concat(transition.RequiredFields.Select(f => f.Name))
                .Where(r => r != null)
                .ToList();

        if (requirements.Count > 0)
        {
            StatusMessageText.Text =
                $"'{transition.Name}' requires {string.Join(" and ", requirements)} -- fill it in below, then click Apply.";
        }
    }

    private async void
    ApplyStatusButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (StatusCombo.SelectedItem is not JiraTransition transition)
        {
            return;
        }

        var commentText = NewCommentTextBox.Text.Trim();

        if (transition.RequiresComment && string.IsNullOrWhiteSpace(commentText))
        {
            StatusMessageText.Text = $"'{transition.Name}' requires a comment -- write it below first.";

            return;
        }

        var missingField = transition.RequiredFields.FirstOrDefault(f => string.IsNullOrWhiteSpace(f.EnteredValue));

        if (missingField != null)
        {
            StatusMessageText.Text = $"'{transition.Name}' requires '{missingField.Name}' -- fill it in above first.";

            return;
        }

        try
        {
            StatusMessageText.Text = "Changing status...";

            await _atlassianService.TransitionJiraIssue(
                _settings,
                _issueKey,
                transition.Id,
                transition.RequiresComment ? commentText : null,
                transition.RequiredFields,
                _assignableUsers);

            StatusMessageText.Text = "Status changed.";

            if (transition.RequiresComment)
            {
                NewCommentTextBox.Clear();

                CommentsItemsControl.ItemsSource = await _atlassianService.GetJiraComments(_settings, _issueKey);
            }

            // The set of legal transitions depends on the status just
            // moved to, so it has to be refetched rather than just
            // trusting whatever was already loaded.
            var detail = await _atlassianService.GetJiraIssueDetail(_settings, _issueKey);

            StatusBadgeText.Text = detail.Status;

            _transitions = await _atlassianService.GetJiraTransitions(_settings, _issueKey);

            StatusCombo.ItemsSource = _transitions;
            StatusCombo.SelectedIndex = _transitions.Count > 0 ? 0 : -1;
        }
        catch (Exception ex)
        {
            Logger.Error("JiraIssueViewerWindow: failed to change status.", ex);

            // Some required fields are enforced by a workflow validator
            // that never shows up in the transition's own screen data or
            // in editmeta's "required" flags -- the only place they're
            // ever named is in the failure message itself. Recovered here
            // by looking that name up (editmeta lists every editable
            // field, not just required ones) and adding it to the form so
            // the next Apply can actually succeed.
            var missingFieldNames = AtlassianService.TryExtractMissingFieldNames(ex.Message);

            if (missingFieldNames.Count > 0)
            {
                var foundFields = await _atlassianService.GetJiraFieldsByName(_settings, _issueKey, missingFieldNames);

                var newFields =
                    foundFields.Where(f => !transition.RequiredFields.Any(rf => rf.FieldId == f.FieldId)).ToList();

                if (newFields.Count > 0)
                {
                    transition.RequiredFields = transition.RequiredFields.Concat(newFields).ToList();
                    RequiredFieldsItemsControl.ItemsSource = transition.RequiredFields;

                    StatusMessageText.Text =
                        $"'{transition.Name}' also requires {string.Join(", ", newFields.Select(f => f.Name))} -- fill it in above, then click Apply again.";

                    return;
                }
            }

            StatusMessageText.Text = $"Could not change status: {ex.Message}";
        }
    }

    private async void
    ApplyAssigneeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var accountId = AssigneeCombo.SelectedValue as string;

        try
        {
            StatusMessageText.Text = "Changing assignee...";

            await _atlassianService.SetJiraAssignee(
                _settings,
                _issueKey,
                string.IsNullOrWhiteSpace(accountId) ? null : accountId);

            StatusMessageText.Text = "Assignee changed.";
            CurrentAssigneeText.Text = $"Currently: {(AssigneeCombo.SelectedItem is NameValue nv ? nv.Display : UnassignedName)}";
        }
        catch (Exception ex)
        {
            Logger.Error("JiraIssueViewerWindow: failed to change assignee.", ex);

            StatusMessageText.Text = $"Could not change assignee: {ex.Message}";
        }
    }

    private async void
    AddCommentButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var text = NewCommentTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            StatusMessageText.Text = "Adding comment...";

            var isPublic = _isServiceDeskIssue && VisibleToCustomerRadio.IsChecked == true;

            await _atlassianService.PostJiraComment(_settings, _issueKey, text, isPublic, _isServiceDeskIssue, _assignableUsers);

            NewCommentTextBox.Clear();

            CommentsItemsControl.ItemsSource = await _atlassianService.GetJiraComments(_settings, _issueKey);

            StatusMessageText.Text = "Comment added.";
        }
        catch (Exception ex)
        {
            Logger.Error("JiraIssueViewerWindow: failed to add comment.", ex);

            StatusMessageText.Text = $"Could not add comment: {ex.Message}";
        }
    }

    private void
    OpenInBrowserButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_fallbackUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_fallbackUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"JiraIssueViewerWindow: failed to open '{_fallbackUrl}'.", ex);

            AppMessageBox.Show(ex.ToString(), "Atlassian Search");
        }
    }
}
