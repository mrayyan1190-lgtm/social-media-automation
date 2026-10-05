using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Infrastructure.Storage;

/// <summary>
/// Development/demo object storage on the local file system ("Storage:Provider" = "Local").
/// ponytail: local disk only; add an Azure Blob implementation of IMediaStorage for staging (TASKS.md Phase 8),
/// which is also where provider-readable temporary delivery URLs (SAS) come from.
/// </summary>
internal sealed class LocalFileMediaStorage(string rootDirectory) : IMediaStorage, IStorageProbe
{
    private readonly string root = Path.GetFullPath(rootDirectory);

    public async Task SaveNewAsync(string objectKey, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var path = Resolve(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // CreateNew: an existing object is never overwritten; a key collision is an error, not a silent replace.
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.WriteAsync(content, cancellationToken);
    }

    /// <summary>System Health: a real write, read and delete of a small probe file under the storage root.</summary>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var path = Resolve($"_health/{Guid.NewGuid():N}.probe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [1, 2, 3], cancellationToken);
        try
        {
            if ((await File.ReadAllBytesAsync(path, cancellationToken)).Length != 3)
                throw new IOException("Storage probe read back different content.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new FileStream(Resolve(objectKey), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    private string Resolve(string objectKey)
    {
        var path = Path.GetFullPath(Path.Combine(root, objectKey));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new InvalidOperationException("Object key escapes the storage root.");
    }
}
