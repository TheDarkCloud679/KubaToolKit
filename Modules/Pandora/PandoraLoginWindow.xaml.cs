using KubaToolKit.Modules.Pandora.Models;
using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace KubaToolKit.Modules.Pandora;

/// Fenêtre de connexion pour les sites Pandora protégés par une SSO
/// OAuth2 : affiche un vrai navigateur (WebView2), laisse l'utilisateur se
/// connecter normalement (identique au flux "aws sso login" côté AWS,
/// mais interactif dans l'appli plutôt que dans un navigateur externe),
/// puis récupère les cookies de session une fois revenu sur le domaine
/// Pandora pour les réutiliser dans les appels API suivants.
public partial class PandoraLoginWindow
    : Window
{
    private readonly string _startUrl;
    private readonly string _pandoraHost;
    private bool _leftPandoraHost;
    private bool _completed;

    public List<PandoraCookie> Cookies { get; private set; } = new();

    public PandoraLoginWindow(
        string startUrl)
    {
        InitializeComponent();

        _startUrl = startUrl;
        _pandoraHost = new Uri(startUrl).Host;

        Loaded += async (_, __) => await InitializeAsync();
    }

    private async Task
    InitializeAsync()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();

            WebView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;

            WebView.CoreWebView2.Navigate(_startUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossible d'initialiser le navigateur intégré (WebView2 Runtime installé ?) :\n\n{ex}",
                "Pandora - erreur de connexion");

            DialogResult = false;
        }
    }

    private async void
    CoreWebView2_SourceChanged(
        object? sender,
        CoreWebView2SourceChangedEventArgs e)
    {
        if (_completed)
        {
            return;
        }

        Uri current;

        try
        {
            current = new Uri(WebView.CoreWebView2.Source);
        }
        catch
        {
            return;
        }

        // On repère d'abord le passage par un domaine autre que Pandora
        // (la SSO) : sans ça, la toute première navigation -- qui est
        // aussi sur le domaine Pandora avant sa redirection -- serait
        // prise à tort pour une connexion déjà réussie.
        if (!string.Equals(current.Host, _pandoraHost, StringComparison.OrdinalIgnoreCase))
        {
            _leftPandoraHost = true;

            return;
        }

        if (!_leftPandoraHost)
        {
            return;
        }

        _completed = true;

        // Filtré sur la racine du domaine plutôt que sur _startUrl (qui
        // inclut le sous-chemin /pandora_console/) : un cookie posé sur un
        // autre chemin pendant le retour de la SSO (page de callback,
        // etc.) doit être capturé aussi, pas seulement ceux qui
        // s'appliquent au chemin exact de départ.
        var rawCookies =
            await WebView.CoreWebView2.CookieManager.GetCookiesAsync(
                $"{current.Scheme}://{current.Host}");

        Cookies =
            rawCookies
                .Select(c =>
                    new PandoraCookie
                    {
                        Name = c.Name,
                        Value = c.Value,
                        Domain = c.Domain,
                        Path = c.Path
                    })
                .ToList();

        DialogResult = true;
    }
}
