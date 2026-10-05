namespace SocialMediaAutomation.Application.Abstractions;

/// <summary>Object storage for media bytes. SQL holds only metadata and the object key.</summary>
public interface IMediaStorage
{
    /// <summary>Writes a new object. Must fail rather than overwrite when the key already exists.</summary>
    Task SaveNewAsync(string objectKey, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
}
