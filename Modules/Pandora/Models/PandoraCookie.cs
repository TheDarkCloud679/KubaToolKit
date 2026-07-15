namespace KubaToolKit.Modules.Pandora.Models;

/// Copie minimale d'un CoreWebView2Cookie -- évite de coupler PandoraService
/// (logique métier) au type WebView2 lui-même, qui n'a de sens que côté
/// fenêtre de connexion.
public class PandoraCookie
{
    public string Name { get; set; } = "";

    public string Value { get; set; } = "";

    public string Domain { get; set; } = "";

    public string Path { get; set; } = "";
}
