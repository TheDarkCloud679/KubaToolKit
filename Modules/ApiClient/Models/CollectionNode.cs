using System.Collections.ObjectModel;

namespace KubaToolKit.Modules.ApiClient.Models;

public class CollectionNode
{
    public string Name { get; set; } = "";
    public bool IsRequest { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public List<HeaderItem> Headers { get; set; } = new();
    public string Body { get; set; } = "";

    // "none" | "formdata" | "urlencoded" | "raw" (le mode "graphql" de
    // Postman n'est pas importé pour l'instant : replié sur "raw" vide).
    public string BodyMode { get; set; } = "raw";
    public List<HeaderItem> BodyFormData { get; set; } = new();

    // ObservableCollection (pas List) : le tri par double-clic réordonne
    // en place à chaque niveau, il faut que le TreeView le voie.
    public ObservableCollection<CollectionNode> Children { get; set; } = new();

    // Non null uniquement pour un nœud racine (une collection) : le
    // fichier .json sur lequel écrire lors d'un ajout/suppression/rename.
    public string? FilePath { get; set; }

    // Référence vers le dossier/collection parent : permet de retrouver la
    // racine (donc le fichier) à sauvegarder et de retirer un nœud de la
    // liste de son parent lors d'une suppression.
    public CollectionNode? Parent { get; set; }

    public string DisplayText =>
        IsRequest ? $"{Method}  {Name}" : Name;

    public CollectionNode
    GetRoot()
    {
        var node = this;

        while (node.Parent != null)
        {
            node = node.Parent;
        }

        return node;
    }
}
