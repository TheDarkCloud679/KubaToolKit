namespace KubaToolKit.Modules.ProjectInfo.Models;

/// Une section libre (Contacts, Équipements réseau, VPN, ou tout autre nom
/// choisi par l'utilisateur) : ses colonnes sont définies par
/// l'utilisateur, pas figées dans le code, pour rester adaptable à
/// n'importe quel type d'info à consigner.
public class ProjectInfoSection
{
    public string Name { get; set; } = "";

    public List<string> Columns { get; set; } = new();

    public List<Dictionary<string, string>> Rows { get; set; } = new();
}
