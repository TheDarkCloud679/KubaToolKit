using KubaToolKit.Modules.Pandora.Models;
using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace KubaToolKit.Modules.Pandora;

/// Fenêtre de connexion pour les sites Pandora protégés par une SSO
/// OAuth2 : affiche un vrai navigateur (WebView2), laisse l'utilisateur se
/// connecter normalement (identique au flux "aws sso login" côté AWS,
/// mais interactif dans l'appli plutôt que dans un navigateur externe),
/// puis récupère les cookies de session une fois la connexion terminée
/// pour les réutiliser dans les appels API suivants.
public partial class PandoraLoginWindow
    : Window
{
    private readonly string _startUrl;
    private readonly string _pandoraHost;
    private bool _sawAuthStep;
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

    private void
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

        // La passerelle SSO peut rediriger vers un domaine externe (IdP
        // séparé) OU rester sur le même hôte que la console (proxy oauth2
        // intégré -- l'URL "oauth2/start?repo=4" vue en erreur est
        // relative, donc probablement sur ce même hôte) : les deux comptent
        // comme "en cours de connexion", pas seulement un changement de
        // domaine.
        bool looksLikeAuthStep =
            !string.Equals(current.Host, _pandoraHost, StringComparison.OrdinalIgnoreCase)
            || current.AbsolutePath.Contains("/oauth2/", StringComparison.OrdinalIgnoreCase);

        if (looksLikeAuthStep)
        {
            _sawAuthStep = true;

            return;
        }

        // De retour sur une page Pandora normale après être passé par une
        // étape de connexion : terminé. Si la détection automatique se
        // trompe (flux SSO différent de ce qui a été observé), le bouton
        // Continuer reste le filet de sécurité.
        if (_sawAuthStep)
        {
            _ = CompleteAsync();
        }
    }

    private async void
    ContinueButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CompleteAsync();
    }

    private async Task
    CompleteAsync()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;

        Uri current;

        try
        {
            current = new Uri(WebView.CoreWebView2.Source);
        }
        catch
        {
            current = new Uri(_startUrl);
        }

        // Filtré sur la racine du domaine plutôt que sur _startUrl (qui
        // inclut le sous-chemin /pandora_console/) : un cookie posé sur un
        // autre chemin pendant le retour de la SSO doit être capturé
        // aussi.
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
