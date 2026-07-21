using KubaToolKit.Shared.Services;
using System.IO;
using System.Xml.Linq;

namespace KubaToolKit.Modules.ProjectInfo;

/// Writes directly into FileZilla's own Site Manager file so exported
/// sites show up next time FileZilla is launched -- a format owned by a
/// third-party app we can't test against live, so every write is preceded
/// by a timestamped backup of whatever was already there, and existing
/// sites/folders are left untouched (only the target folder's contents
/// are replaced). Close FileZilla before exporting: it only writes this
/// file back out on its own exit, so a concurrent FileZilla session could
/// overwrite what we just wrote.
public static class FileZillaSiteManagerService
{
    public static string
    GetSiteManagerPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileZilla",
            "sitemanager.xml");

    public readonly record struct SiteEntry(string Name, string Host);

    public static void
    ExportFolder(
        string folderName,
        IReadOnlyList<SiteEntry> entries,
        string username,
        string port,
        string keyFilePath)
    {
        var path = GetSiteManagerPath();
        var directory = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(directory);

        XDocument doc;

        if (File.Exists(path))
        {
            var backupPath =
                Path.Combine(directory, $"sitemanager.xml.bak-{DateTime.Now:yyyyMMdd-HHmmss}");

            File.Copy(path, backupPath);

            Logger.Debug($"FileZillaSiteManagerService: backed up {path} to {backupPath}.");

            doc = XDocument.Load(path);
        }
        else
        {
            doc = new XDocument(new XElement("FileZilla3", new XAttribute("version", "3")));
        }

        var root =
            doc.Root
            ?? throw new InvalidOperationException("sitemanager.xml has no root element.");

        var servers = root.Element("Servers");

        if (servers == null)
        {
            servers = new XElement("Servers");
            root.Add(servers);
        }

        // A Folder's name is its own leading text node, mixed with the
        // <Server> children -- not an attribute or a dedicated child
        // element.
        var existingFolder =
            servers.Elements("Folder")
                .FirstOrDefault(f =>
                    string.Equals(
                        f.Nodes().OfType<XText>().FirstOrDefault()?.Value.Trim(),
                        folderName,
                        StringComparison.OrdinalIgnoreCase));

        existingFolder?.Remove();

        var folder = new XElement("Folder", new XAttribute("expanded", "1"), folderName);

        foreach (var entry in entries)
        {
            folder.Add(BuildServerElement(entry, username, port, keyFilePath));
        }

        servers.Add(folder);

        doc.Save(path);

        Logger.Info(
            $"FileZillaSiteManagerService: exported {entries.Count} site(s) to folder \"{folderName}\" in {path}.");
    }

    private static XElement
    BuildServerElement(
        SiteEntry entry,
        string username,
        string port,
        string keyFilePath)
    {
        var server = new XElement(
            "Server",
            new XElement("Host", entry.Host),
            new XElement("Port", port),
            new XElement("Protocol", "1"), // SFTP
            new XElement("Type", "0"),
            new XElement("User", username),
            new XElement("Logontype", "1")); // Normal -- key file (below) takes over the actual auth

        if (!string.IsNullOrWhiteSpace(keyFilePath))
        {
            server.Add(new XElement("Keyfile", keyFilePath));
        }

        server.Add(
            new XElement("PasvMode", "MODE_DEFAULT"),
            new XElement("MaximumMultipleConnections", "0"),
            new XElement("EncodingType", "Auto"),
            new XElement("BypassProxy", "0"),
            new XElement("Name", entry.Name));

        return server;
    }
}
