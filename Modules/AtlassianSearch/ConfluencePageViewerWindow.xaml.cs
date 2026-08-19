using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Diagnostics;
using System.Windows;
using KubaToolKit.Shared.Windows;

namespace KubaToolKit.Modules.AtlassianSearch;

// A lightweight in-app viewer so opening a search hit doesn't require
// leaving the app -- Confluence's own rendered HTML (body.view) is shown
// via the legacy WebBrowser control (already part of WPF, no extra
// dependency). It can't authenticate image/attachment requests the way a
// real signed-in browser tab would, so an "Open in browser" fallback is
// always available alongside it.
public partial class ConfluencePageViewerWindow
    : Window
{
    // Confluence's body.view HTML is written expecting the site's own
    // stylesheet (macros, panels, code blocks...), which isn't something
    // we can pull in wholesale -- this approximates the common view
    // classes instead. Kept to classic box-model CSS (no flex/grid),
    // since the WebBrowser control renders with the old Trident engine.
    private const string ConfluenceCss =
        """
        body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 14px; color: #172B4D; line-height: 1.5; padding: 4px; }
        h1, h2, h3, h4, h5, h6 { font-family: 'Segoe UI', Arial, sans-serif; font-weight: 600; color: #172B4D; margin-top: 20px; margin-bottom: 8px; }
        h1 { font-size: 24px; }
        h2 { font-size: 20px; }
        h3 { font-size: 17px; }
        h4 { font-size: 15px; }
        a { color: #0052CC; text-decoration: none; }
        a:hover { text-decoration: underline; }
        img { max-width: 100%; height: auto; }
        .image-wrap { display: inline-block; max-width: 100%; }
        table, table.confluenceTable, table.wrapped { border-collapse: collapse; margin: 8px 0; }
        table th, table td, th.confluenceTh, td.confluenceTd { border: 1px solid #DFE1E6; padding: 6px 10px; vertical-align: top; }
        th.confluenceTh, table th { background-color: #F4F5F7; font-weight: 600; text-align: left; }
        .table-wrap { overflow-x: auto; }
        code, tt { font-family: Consolas, monospace; background-color: #F4F5F7; padding: 1px 4px; border-radius: 3px; font-size: 13px; }
        pre, .code, .codeContent, .preformatted { font-family: Consolas, monospace; background-color: #F4F5F7; border: 1px solid #DFE1E6; border-radius: 3px; padding: 10px; overflow-x: auto; font-size: 13px; white-space: pre-wrap; }
        .panel { border: 1px solid #DFE1E6; border-radius: 3px; margin: 10px 0; }
        .panelHeader { padding: 6px 10px; font-weight: 600; border-bottom: 1px solid #DFE1E6; background-color: #F4F5F7; }
        .panelContent { padding: 10px; }
        .confluence-information-macro { border-radius: 3px; margin: 10px 0; padding: 10px 12px; border-left: 4px solid #8993A4; background-color: #F4F5F7; }
        .confluence-information-macro-information, .confluence-information-macro-note { border-left-color: #0052CC; background-color: #DEEBFF; }
        .confluence-information-macro-tip { border-left-color: #00875A; background-color: #E3FCEF; }
        .confluence-information-macro-warning { border-left-color: #FF991F; background-color: #FFFAE6; }
        .confluence-information-macro-error { border-left-color: #DE350B; background-color: #FFEBE6; }
        .confluence-information-macro-body { margin: 0; }
        .confluence-information-macro .aui-icon { display: none; }
        .expand-container { border: 1px solid #DFE1E6; border-radius: 3px; margin: 10px 0; }
        .expand-control, .expand-control-text { padding: 8px 10px; font-weight: 600; background-color: #F4F5F7; }
        .expand-content { padding: 10px; border-top: 1px solid #DFE1E6; }
        ul.taskList, ul.task-list { list-style: none; padding-left: 4px; }
        .task-list li, li.task-list-item { margin-bottom: 4px; }
        blockquote { border-left: 3px solid #DFE1E6; margin: 10px 0; padding: 4px 12px; color: #42526E; }
        hr { border: none; border-top: 1px solid #DFE1E6; margin: 16px 0; }
        .status-macro { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 11px; font-weight: 700; background-color: #DFE1E6; }
        """;

    private readonly AtlassianService _atlassianService;
    private readonly AtlassianSettings _settings;
    private readonly string _pageId;
    private readonly string _fallbackUrl;

    public ConfluencePageViewerWindow(
        AtlassianService atlassianService,
        AtlassianSettings settings,
        string pageId,
        string title,
        string fallbackUrl)
    {
        InitializeComponent();

        _atlassianService = atlassianService;
        _settings = settings;
        _pageId = pageId;
        _fallbackUrl = fallbackUrl;

        TitleText.Text = title;
        OpenInBrowserButton.IsEnabled = !string.IsNullOrWhiteSpace(fallbackUrl);

        Loaded += async (_, __) => await LoadAsync();
    }

    private async Task
    LoadAsync()
    {
        try
        {
            var content = await _atlassianService.GetConfluencePageContent(_settings, _pageId);

            if (!string.IsNullOrWhiteSpace(content.Title))
            {
                TitleText.Text = content.Title;
            }

            var baseUrl = _settings.BaseUrl.TrimEnd('/');

            // A <base> tag so any image/link the inliner didn't catch
            // (e.g. plain links, not "src=") still resolves against the
            // real site instead of being treated as relative to nothing.
            var html =
                "<html><head>"
                + $"<base href=\"{baseUrl}/wiki/\"/>"
                + "<meta charset=\"utf-8\"/>"
                + $"<style>{ConfluenceCss}</style>"
                + "</head><body>"
                + content.Html
                + "</body></html>";

            ContentBrowser.NavigateToString(html);
            ContentBrowser.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;

            // The legacy WebBrowser control's underlying ActiveX site is
            // created lazily on this first navigation -- that steals
            // window activation, dropping this window behind whatever the
            // user clicked into while the page was loading.
            Activate();
        }
        catch (Exception ex)
        {
            Logger.Error("ConfluencePageViewerWindow: failed to load page content.", ex);

            StatusText.Text =
                string.IsNullOrWhiteSpace(_fallbackUrl)
                    ? $"Could not load this page's content ({ex.Message})."
                    : $"Could not load this page's content ({ex.Message}). Use \"Open in browser\" instead.";
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
            Logger.Error($"ConfluencePageViewerWindow: failed to open '{_fallbackUrl}'.", ex);

            AppMessageBox.Show(ex.ToString(), "Atlassian Search");
        }
    }
}
