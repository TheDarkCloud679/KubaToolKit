using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using KubaToolKit.Modules.S3Explorer.Models;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;
using System.IO;
using System.IO.Compression;
using System.Text;
using Amazon.S3.Transfer;

namespace KubaToolKit.Modules.S3Explorer;



public class S3Service
{
    public async Task<List<string>>
        GetBuckets(
            string profile)
    {
        var chain =
            new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(
                profile,
                out var credentials))
        {
            throw new Exception(
                $"Unable to load AWS profile '{profile}'");
        }

        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);

        var response =
            await client
                .ListBucketsAsync();

        return response.Buckets
            .Select(x => x.BucketName)
            .OrderBy(x => x)
            .ToList();
    }

    private Amazon.Runtime.AWSCredentials
GetCredentials(
    string profile)
    {
        var chain =
            new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(
                profile,
                out var credentials))
        {
            throw new Exception(
                $"Unable to load AWS profile '{profile}'");
        }

        return credentials;
    }

    public async Task<List<string>>
    GetFolders(
        string profile,
        string bucketName,
        string prefix = "")
    {
        var chain =
            new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(
                profile,
                out var credentials))
        {
            throw new Exception(
                $"Unable to load AWS profile '{profile}'");
        }
        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);
        var request =
            new ListObjectsV2Request
            {
                BucketName =
                    bucketName,
                Prefix =
                    prefix,
                Delimiter =
                    "/"
            };
        var response =
            await client
                .ListObjectsV2Async(
                    request);
        return response
            .CommonPrefixes?
            .OrderBy(x => x)
            .ToList()
            ?? new List<string>();
    }

    public async Task<List<ArchiveEntryItem>>
GetArchiveEntries(
    string profile,
    string bucket,
    string key)
    {
        var credentials =
            GetCredentials(
                profile);

        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);

        using var response =
            await client
                .GetObjectAsync(
                    bucket,
                    key);

        using var memoryStream =
            new MemoryStream();

        await response
            .ResponseStream
            .CopyToAsync(
                memoryStream);

        memoryStream.Position =
            0;

        return ReadArchiveEntries(
            memoryStream,
            key);
    }

    public async Task<string?>
GetFileContent(
    string profile,
    string bucket,
    string key,
    IProgress<int>? progress = null)
    {
        var credentials =
            GetCredentials(
                profile);

        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);

        using var response =
            await client
                .GetObjectAsync(
                    bucket,
                    key);

        using var memoryStream =
            new MemoryStream();

        var totalBytes =
            response.ContentLength;

        var buffer =
            new byte[81920];

        long totalRead =
            0;

        int bytesRead;

        while ((bytesRead =
            await response
                .ResponseStream
                .ReadAsync(
                    buffer,
                    0,
                    buffer.Length)) > 0)
        {
            await memoryStream
                .WriteAsync(
                    buffer,
                    0,
                    bytesRead);

            totalRead +=
                bytesRead;

            int percent =
                totalBytes > 0
                    ? (int)(
                        totalRead
                        * 100
                        / totalBytes)
                    : 0;

            progress?.Report(
                percent);
        }

        memoryStream.Position =
            0;

        var extension =
            Path.GetExtension(
                key)
            .ToLowerInvariant();

        switch (extension)
        {
            case ".gz":
                return ReadGzipContent(
                    memoryStream);

            case ".zip":
            case ".7z":
            case ".rar":
                return "__ARCHIVE__";

            default:
                memoryStream.Position =
                    0;

                using (var reader =
                    new StreamReader(
                        memoryStream,
                        Encoding.UTF8,
                        true,
                        leaveOpen: true))
                {
                    return await reader
                        .ReadToEndAsync();
                }
        }
    }

    private List<ArchiveEntryItem>
        Read7ZipEntries(
            Stream stream,
            string archivePath)
    {
        stream.Position =
            0;

        var rootItems =
            new List<ArchiveEntryItem>();

        using var archive =
            SevenZipArchive
                .OpenArchive(
                    stream,
                    ReaderOptions.ForExternalStream);

        foreach (var entry
                 in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            var path =
                entry.Key
                ?? string.Empty;

            AddToTree(
                rootItems,
                archivePath,
                path);
        }
        return rootItems;
    }

    private List<ArchiveEntryItem>
    ReadArchiveEntries(
        Stream stream,
        string archivePath)
    {
        stream.Position =
            0;

        try
        {
            using var reader =
                ReaderFactory
                    .OpenReader(
                        stream,
                        ReaderOptions.ForExternalStream);

            var rootItems =
                new List<ArchiveEntryItem>();

            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory)
                {
                    continue;
                }

                var path =
                    reader.Entry.Key
                    ?? string.Empty;

                AddToTree(
                    rootItems,
                    archivePath,
                    path);
            }

            return rootItems;
        }
        catch
        {
            return Read7ZipEntries(
                stream,
                archivePath);
        }
    }

    public async Task<string>
ReadArchiveFile(
    string profile,
    string bucketName,
    string archiveKey,
    string entryPath,
    IProgress<int>? progress = null)
    {
        var credentials =
            GetCredentials(
                profile);

        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);

        using var response =
            await client
                .GetObjectAsync(
                    bucketName,
                    archiveKey);

        using var memoryStream =
            new MemoryStream();

        var totalBytes =
            response.ContentLength;

        var buffer =
            new byte[81920];

        long totalRead =
            0;

        int bytesRead;

        while ((bytesRead =
            await response
                .ResponseStream
                .ReadAsync(
                    buffer,
                    0,
                    buffer.Length)) > 0)
        {
            await memoryStream
                .WriteAsync(
                    buffer,
                    0,
                    bytesRead);

            totalRead +=
                bytesRead;

            int percent =
                totalBytes > 0
                    ? (int)(
                        totalRead
                        * 100
                        / totalBytes)
                    : 0;

            progress?.Report(
                percent);
        }

        memoryStream.Position =
            0;

        try
        {
            return await ReadEntryContent(
                memoryStream,
                entryPath);
        }
        catch (Exception ex)
        {
            return
                $"ERROR:\n{ex}";
        }
    }

    /// Lit le contenu d'une entrée quel que soit le format de l'archive
    /// (zip, 7z, rar, tar...) au lieu de se limiter au zip natif .NET,
    /// pour rester cohérent avec ReadArchiveEntries qui liste déjà ces
    /// formats via SharpCompress.
    private async Task<string>
    ReadEntryContent(
        Stream stream,
        string entryPath)
    {
        stream.Position =
            0;

        try
        {
            using var reader =
                ReaderFactory
                    .OpenReader(
                        stream,
                        ReaderOptions.ForExternalStream);

            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory
                    ||
                    reader.Entry.Key
                    != entryPath)
                {
                    continue;
                }

                using var entryStream =
                    reader.OpenEntryStream();

                using var textReader =
                    new StreamReader(
                        entryStream);

                return await textReader
                    .ReadToEndAsync();
            }

            return
                "File not found in archive.";
        }
        catch
        {
            return Read7ZipEntryContent(
                stream,
                entryPath);
        }
    }

    private string
    Read7ZipEntryContent(
        Stream stream,
        string entryPath)
    {
        stream.Position =
            0;

        using var archive =
            SevenZipArchive
                .OpenArchive(
                    stream,
                    ReaderOptions.ForExternalStream);

        var entry =
            archive.Entries
                .FirstOrDefault(x =>
                    !x.IsDirectory
                    && x.Key == entryPath);

        if (entry == null)
        {
            return
                "File not found in archive.";
        }

        using var entryStream =
            entry.OpenEntryStream();

        using var reader =
            new StreamReader(
                entryStream);

        return reader
            .ReadToEnd();
    }

    public async Task<List<S3ObjectItem>>
    SearchFiles(
        string profile,
        string bucket,
        string prefix,
        string searchText,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
    var credentials =
        GetCredentials(
            profile);

    using var client =
        new AmazonS3Client(
            credentials,
            RegionEndpoint.EUWest3);

    var results =
        new List<S3ObjectItem>();

        int
    pagesProcessed =
        0;

        const int
        MaxResults = 100;

        string?
        continuationToken =
            null;

    do
    {
            cancellationToken
           .ThrowIfCancellationRequested();
            pagesProcessed++;
            progress?.Report(pagesProcessed);
            var request =
            new ListObjectsV2Request
            {
                BucketName =
                    bucket,

                Prefix =
                    prefix ?? "",

                ContinuationToken =
                    continuationToken
            };

        var response =
            await client
                .ListObjectsV2Async(
                    request);

        results.AddRange(
            response.S3Objects
                .Where(x =>
                    x.Key.Contains(
                        searchText,
                        StringComparison
                            .OrdinalIgnoreCase))
                .Select(x =>
                    new S3ObjectItem
                    {
                        Name =
        x.Key
            .Split('/')
            .Last(),
                        Key =
        x.Key,
                       Size =
        x.Size
        ?? 0,

                        LastModified =
        x.LastModified
        ?? DateTime.MinValue
                    }));

            if (results.Count >= MaxResults) {break;}

            continuationToken =
                response.IsTruncated == true
                    ? response.NextContinuationToken
                    : null;

        }
    while (continuationToken != null);

        return results
        .Take(MaxResults)
        .OrderBy(x => x.Name)
        .ToList();
    }


    private void
     AddToTree(
         List<ArchiveEntryItem> rootItems,
         string archivePath,
         string fullPath)
    {
        var parts =
            fullPath.Split(
                new[]
                {
                '/',
                '\\'
                },
                StringSplitOptions
                    .RemoveEmptyEntries);

        List<ArchiveEntryItem>
            current =
                rootItems;

        ArchiveEntryItem?
            currentItem =
                null;

        for (int i = 0;
             i < parts.Length;
             i++)
        {
            var part =
                parts[i];

            var isLast =
                i ==
                parts.Length - 1;

            var existing =
                current
                    .FirstOrDefault(
                        x =>
                            x.Name
                            == part);

            if (existing == null)
            {
                existing =
                    new ArchiveEntryItem
                    {
                        Name =
                            part,

                        FullPath =
                            fullPath,

                        IsDirectory =
                            !isLast,

                        ArchivePath =
                            archivePath,

                        EntryPath =
                            fullPath
                    };

                current.Add(
                    existing);
            }

            currentItem =
                existing;

            current =
                currentItem
                    .Children;
        }
    }

    private string
    ReadZipContent(
        Stream stream)
    {
        using var archive =
            new ZipArchive(
                stream,
                ZipArchiveMode.Read,
                true);

        var supportedExtensions =
            new[]
            {
            ".json",
            ".txt",
            ".log",
            ".xml",
            ".csv"
            };

        var entry =
            archive
                .Entries
                .FirstOrDefault(
                    x =>
                        supportedExtensions
                            .Contains(
                                Path.GetExtension(
                                    x.FullName)
                                .ToLowerInvariant()));

        if (entry == null)
        {
            return
                "No readable file found inside ZIP.";
        }

        using var entryStream =
            entry.Open();

        using var reader =
            new StreamReader(
                entryStream);

        return reader
            .ReadToEnd();
    }

    private string
    ReadGzipContent(
        Stream stream)
    {
        using var gzip =
            new GZipStream(
                stream,
                CompressionMode.Decompress);

        using var reader =
            new StreamReader(
                gzip);

        return reader
            .ReadToEnd();
    }

    public async Task
UploadFile(
    string profile,
    string bucket,
    string key,
    string localPath,
    IProgress<int>? progress = null,
    CancellationToken cancellationToken = default)
    {
        var credentials =
            GetCredentials(
                profile);

        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);

        using var transfer =
            new TransferUtility(
                client);

        var request =
            new TransferUtilityUploadRequest
            {
                BucketName = bucket,
                Key = key,
                FilePath = localPath
            };

        request.UploadProgressEvent +=
            (_, e) =>
            {
                progress?.Report(
                    e.PercentDone);
            };

        await transfer.UploadAsync(
            request,
            cancellationToken);
    }

    public async Task
    DownloadFile(
        string profile,
        string bucket,
        string key,
        string localPath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var credentials =
            GetCredentials(
                profile);

        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);

        using var response =
            await client
                .GetObjectAsync(
                    bucket,
                    key);

        using var fileStream =
            new FileStream(
                localPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

        var totalBytes =
            response.ContentLength;

        var buffer =
            new byte[81920];

        long totalRead =
            0;

        int bytesRead;

        cancellationToken
    .ThrowIfCancellationRequested();

        while ((bytesRead =
    await response
        .ResponseStream
        .ReadAsync(
            buffer,
            0,
            buffer.Length,
            cancellationToken)) > 0)
        {
            await fileStream
    .WriteAsync(
        buffer,
        0,
        bytesRead,
        cancellationToken);

            totalRead +=
                bytesRead;

            int percent =
                totalBytes > 0
                    ? (int)(
                        totalRead
                        * 100
                        / totalBytes)
                    : 0;
            progress?.Report(
                percent);
        }
    }

    public async Task
RenameFile(
    string profile,
    string bucket,
    string key,
    string newName)
    {
        var credentials =
            GetCredentials(
                profile);

        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);

        var newKey =
            Path.Combine(
                Path.GetDirectoryName(
                    key)!
                    .Replace("\\", "/"),
                newName)
            .Replace("\\", "/");

        await client.CopyObjectAsync(
            bucket,
            key,
            bucket,
            newKey);

        await client.DeleteObjectAsync(
            bucket,
            key);
    }

    public async Task
DeleteFile(
    string profile,
    string bucket,
    string key)
    {
        var credentials =
            GetCredentials(
                profile);

        using var client =
            new AmazonS3Client(
                credentials,
                RegionEndpoint.EUWest3);

        await client
            .DeleteObjectAsync(
                bucket,
                key);
    }

    public async Task<List<S3ObjectItem>>
    GetFiles(
        string profile,
        string bucketName,
        string prefix)
    {
        var chain = new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(
                profile,
                out var credentials))
        {
            throw new Exception(
                $"Unable to load AWS profile '{profile}'");
        }

        using var client = new AmazonS3Client(credentials, RegionEndpoint.EUWest3);
        var request = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = prefix,
                Delimiter = "/"
            };

        var response = await client.ListObjectsV2Async(request);

        return response
    .S3Objects?
    .Where(x => x.Key != prefix)
    .Select(x => new S3ObjectItem
        {
            Name =
                x.Key
                    .Split('/')
                    .Last(),
            Key =
                x.Key,
            Size =
                x.Size
                ?? 0,
            LastModified =
                x.LastModified
                ?? DateTime.MinValue
        })
    .OrderBy(x => x.Name)
    .ToList()
    ?? new List<S3ObjectItem>();
    }
}