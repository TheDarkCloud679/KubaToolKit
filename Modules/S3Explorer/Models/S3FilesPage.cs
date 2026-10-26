namespace KubaToolKit.Modules.S3Explorer.Models;

// One ListObjectsV2 page -- NextContinuationToken is null once there's
// nothing left to load, which is what tells the caller whether to offer
// "Load more" at all.
public class S3FilesPage
{
    public List<S3ObjectItem> Items { get; set; } = new();

    public string? NextContinuationToken { get; set; }
}
