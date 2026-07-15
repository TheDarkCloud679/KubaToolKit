using KubaToolKit.Modules.Pandora.Models;
using System.Windows;

namespace KubaToolKit.Modules.Pandora;

/// Fenêtre de connexion pour les sites Pandora protégés par une SSO
/// OAuth2 (parfois suivie d'un second formulaire de connexion propre à
/// Pandora, indépendant de la SSO) : affiche un vrai navigateur (WebView2)
/// et laisse l'utilisateur enchaîner toutes les étapes nécessaires
/// normalement. La fermeture n'est jamais automatique -- deviner "la
/// connexion est terminée" depuis la navigation s'est révélé peu fiable
/// (flux à un ou deux formulaires selon le site, redirections internes au
/// même domaine...) -- c'est le clic sur Continuer, une fois la vraie page
/// Pandora affichée, qui capture les cookies de session pour les appels
/// API suivants.
public partial class PandoraLoginWindow
    : Window
{
    private readonly string _startUrl;

    public List<PandoraCookie> Cookies { get; private set; } = new();

    public PandoraLoginWindow(
        string startUrl)
    {
        InitializeComponent();

        _startUrl = startUrl;

        Loaded += async (_, __) => await InitializeAsync();
    }

    private async Task
    InitializeAsync()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();

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
    ContinueButton_Click(
        object sender,
        RoutedEventArgs e)
    {
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
        // autre chemin pendant la connexion doit être capturé aussi.
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
