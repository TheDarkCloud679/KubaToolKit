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

    // Favori (requêtes uniquement) : remonté en tête de sa fratrie par
    // SortNodes, persisté via un champ non standard dans le fichier de
    // collection (ignoré sans risque par Postman lui-même).
    public bool IsFavorite { get; set; }

    // True uniquement pour le pseudo-dossier "Favoris" inséré en tête de
    // chaque collection par RebuildFavoritesFolders : un regroupement
    // purement visuel (mêmes références de nœuds que leur vrai
    // emplacement), jamais sérialisé ni éditable directement.
    public bool IsFavoritesFolder { get; set; }

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
        (IsFavorite ? "⭐ " : "") + (IsRequest ? $"{Method}  {Name}" : Name);

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
