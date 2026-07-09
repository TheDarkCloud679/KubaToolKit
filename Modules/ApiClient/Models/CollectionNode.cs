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

    // Règles d'extraction post-réponse (équivalent simplifié d'un script
    // Postman "pm.environment.set(...)") : Key = nom du champ JSON de
    // premier niveau dans le corps de la réponse, Value = nom de la
    // variable de l'environnement sélectionné à créer/mettre à jour avec
    // sa valeur après un envoi réussi.
    public List<HeaderItem> PostResponseExtractions { get; set; } = new();

    // Valable pour n'importe quel nœud (requête, dossier ou collection) :
    // AuthType.Inherit remonte via Parent jusqu'au premier ancêtre qui
    // définit un auth concret, comme Postman ("Inherit auth from parent").
    public AuthConfig Auth { get; set; } = new();

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

    /// Remonte l'arbre jusqu'au premier ancêtre (inclus) dont l'auth n'est
    /// pas "Inherit" ; retourne "None" si personne dans la chaîne n'en
    /// définit un (comme Postman quand rien n'est configuré nulle part).
    public AuthConfig
    ResolveEffectiveAuth()
    {
        var node = this;

        while (node != null)
        {
            if (node.Auth.Type != AuthType.Inherit)
            {
                return node.Auth;
            }

            node = node.Parent;
        }

        return new AuthConfig { Type = AuthType.None };
    }
}
