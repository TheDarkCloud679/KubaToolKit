namespace KubaToolKit.Modules.ApiClient.Models;

public class EnvironmentSet
{
    public string Name { get; set; } = "";

    // Fichier d'origine (export Postman) : permet de réécrire uniquement
    // ce fichier quand l'utilisateur modifie une valeur dans l'éditeur.
    public string FilePath { get; set; } = "";

    // Toutes les variables (activées ou non), pas seulement celles utilisées
    // pour la substitution : l'éditeur doit pouvoir les afficher/basculer
    // toutes, y compris celles désactivées dans l'export Postman.
    public List<HeaderItem> Variables { get; set; } = new();

    public Dictionary<string, string> ToSubstitutionMap() =>
        Variables
            .Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.Key))
            .GroupBy(v => v.Key)
            .ToDictionary(g => g.Key, g => g.Last().Value);
}
