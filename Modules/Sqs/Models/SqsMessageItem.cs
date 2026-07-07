namespace KubaToolKit.Modules.Sqs.Models;

public class SqsMessageItem
{
    public string MessageId { get; set; } = "";
    public string SentTimestamp { get; set; } = "";
    public string Body { get; set; } = "";

    // Un TextBlock affiche les retours à la ligne réels au lieu de les
    // ignorer : un corps JSON indenté ne montrerait alors que sa première
    // ligne ("{") dans la grille. On aplatit donc pour l'aperçu, tout en
    // gardant Body intact pour la vue détaillée (JsonViewerWindow).
    public string BodyPreview =>
        Body.Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ");
}
