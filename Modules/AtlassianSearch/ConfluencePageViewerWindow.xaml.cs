using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Diagnostics;
using System.Windows;

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

            // A <base> tag so the page's relative image/link URLs (e.g.
            // "/wiki/download/attachments/...") resolve against the real
            // site instead of being treated as relative to nothing.
            var html =
                "<html><head>"
                + $"<base href=\"{baseUrl}/wiki/\"/>"
                + "<meta charset=\"utf-8\"/>"
                + "<style>body { font-family: Segoe UI, sans-serif; font-size: 14px; }</style>"
                + "</head><body>"
                + content.Html
                + "</body></html>";

            ContentBrowser.NavigateToString(html);
            ContentBrowser.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;
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

            MessageBox.Show(ex.ToString(), "Atlassian Search");
        }
    }
}
