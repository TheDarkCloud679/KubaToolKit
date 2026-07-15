using KubaToolKit.Modules.ProjectInfo.Models;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Modules.ProjectInfo;

/// Un seul fichier JSON partagé (Config/project-info.json) plutôt qu'une
/// base de données : pas de concurrence sérieuse attendue pour un petit
/// outil d'équipe, et un fichier texte reste facile à copier/versionner/
/// inspecter à la main. Dernier enregistrement gagnant en cas d'édition
/// simultanée par deux personnes -- acceptable ici, pas de verrouillage.
public class ProjectInfoService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    public static string
    GetFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "project-info.json");

    public ProjectInfoRoot
    Load()
    {
        var filePath = GetFilePath();

        if (!File.Exists(filePath))
        {
            Logger.Debug($"ProjectInfoService: {filePath} absent, démarrage à vide.");

            return new ProjectInfoRoot();
        }

        try
        {
            var json = File.ReadAllText(filePath);

            var root =
                JsonSerializer.Deserialize<ProjectInfoRoot>(json, SerializerOptions)
                ?? new ProjectInfoRoot();

            Logger.Debug($"ProjectInfoService: {root.Projects.Count} projet(s) chargé(s) depuis {filePath}.");

            return root;
        }
        catch (Exception ex)
        {
            Logger.Error($"ProjectInfoService: échec de la lecture de {filePath}.", ex);

            throw;
        }
    }

    public void
    Save(
        ProjectInfoRoot root)
    {
        var filePath = GetFilePath();

        try
        {
            var directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(root, SerializerOptions));

            Logger.Debug($"ProjectInfoService: {root.Projects.Count} projet(s) sauvegardé(s) dans {filePath}.");
        }
        catch (Exception ex)
        {
            Logger.Error($"ProjectInfoService: échec de l'écriture de {filePath}.", ex);

            throw;
        }
    }

    /// Clé de projet effective pour ce profil : celle assignée
    /// explicitement (partagée avec d'autres profils, ex: prod/preprod/
    /// test d'un même projet) si elle existe, sinon le nom du profil
    /// lui-même -- comportement par défaut inchangé tant que l'utilisateur
    /// n'a pas explicitement demandé un partage.
    public string
    ResolveProjectKey(
        ProjectInfoRoot root,
        string profileName) =>
        root.ProfileProjectKeys.TryGetValue(profileName, out var key)
        && !string.IsNullOrWhiteSpace(key)
            ? key
            : profileName;

    public void
    SetProjectKey(
        ProjectInfoRoot root,
        string profileName,
        string projectKey)
    {
        root.ProfileProjectKeys[profileName] = projectKey;
    }

    public ProjectInfoProject
    GetOrCreateProject(
        ProjectInfoRoot root,
        string projectKey)
    {
        var project =
            root.Projects.FirstOrDefault(p =>
                string.Equals(p.Key, projectKey, StringComparison.OrdinalIgnoreCase));

        if (project != null)
        {
            return project;
        }

        project = new ProjectInfoProject { Key = projectKey };
        root.Projects.Add(project);

        return project;
    }

    /// Colonnes de départ pour les types de section les plus courants,
    /// modifiables ensuite librement (renommer/ajouter/retirer des
    /// colonnes) une fois la section créée.
    public static readonly Dictionary<string, string[]> SectionPresets =
        new()
        {
            ["Contacts"] = new[] { "Prénom", "Nom", "Fonction", "Email", "Téléphone" },
            ["Network equipment"] = new[] { "Nom", "Type", "Adresse IP", "Emplacement", "Notes" },
            ["VPN"] = new[] { "Nom", "Type", "Adresse", "Identifiants", "Notes" },
            ["Custom"] = new[] { "Column 1" }
        };
}
