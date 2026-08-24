namespace KubaToolKit.Shared.Services;

// Empty = every module below stays local to this one machine. Pointed at
// a folder a sync client (Google Drive, OneDrive...) already keeps up to
// date, the whole team reads and writes the same data automatically --
// each module gets its own named subfolder underneath (see
// TeamSharingFolders). Nothing that can hold secrets -- API Client's
// Environments, or any of this app's own credentials -- is ever
// redirected here.
public class TeamSharingSettings
{
    public string SharedFolder { get; set; } = "";
}
