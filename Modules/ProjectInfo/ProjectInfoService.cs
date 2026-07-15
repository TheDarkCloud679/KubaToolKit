using KubaToolKit.Modules.ProjectInfo.Models;
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
            return new ProjectInfoRoot();
        }

        var json = File.ReadAllText(filePath);

        return
            JsonSerializer.Deserialize<ProjectInfoRoot>(json, SerializerOptions)
            ?? new ProjectInfoRoot();
    }

    public void
    Save(
        ProjectInfoRoot root)
    {
        var filePath = GetFilePath();
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            filePath,
            JsonSerializer.Serialize(root, SerializerOptions));
    }

    public ProjectInfoProject
    GetOrCreateProject(
        ProjectInfoRoot root,
        string profileName)
    {
        var project =
            root.Projects.FirstOrDefault(p =>
                string.Equals(p.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));

        if (project != null)
        {
            return project;
        }

        project = new ProjectInfoProject { ProfileName = profileName };
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
