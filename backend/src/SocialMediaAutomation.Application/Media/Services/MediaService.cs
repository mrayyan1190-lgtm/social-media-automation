using System.Security.Cryptography;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Media.DTOs;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Media.Services;

public sealed class MediaService(
    OrganizationAuthorizer authorizer,
    IMediaRepository media,
    IMediaStorage storage,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Stores an uploaded image under a new, never-reused object key, then records its metadata. The type and size
    /// come from the bytes themselves; the client's file name is kept only as a label.
    /// </summary>
    public async Task<MediaAssetDto> UploadAsync(Guid organizationId, string? fileName, Stream content, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageCampaigns, cancellationToken);

        var bytes = await ReadBoundedAsync(content, cancellationToken);
        var image = ImageProbe.Inspect(bytes.Span)
            ?? throw Guard.Invalid("Image", "must be a valid JPEG or PNG file");

        var id = Guid.NewGuid();
        var objectKey = $"organizations/{access.OrganizationId:N}/media/{id:N}{image.Extension}";
        var asset = MediaAsset.CreateUpload(
            id, access.OrganizationId, fileName, image.ContentType, bytes.Length, image.Width, image.Height,
            Convert.ToHexStringLower(SHA256.HashData(bytes.Span)), objectKey, access.UserId, timeProvider.GetUtcNow().UtcDateTime);

        // Bytes first: if the metadata insert then fails, an unreferenced object remains, never a row without bytes.
        await storage.SaveNewAsync(objectKey, bytes, asset.ContentType, cancellationToken);
        media.Add(asset);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MediaAssetDto.From(asset);
    }

    public async Task<IReadOnlyList<MediaLibraryItemDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return await media.ListAsync(access.OrganizationId, cancellationToken);
    }

    public async Task<MediaAssetDto> GetAsync(Guid organizationId, Guid mediaAssetId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return MediaAssetDto.From(await LoadAsync(access, mediaAssetId, cancellationToken));
    }

    public async Task<(Stream Content, string ContentType)> OpenContentAsync(Guid organizationId, Guid mediaAssetId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var asset = await LoadAsync(access, mediaAssetId, cancellationToken);
        return (await storage.OpenReadAsync(asset.ObjectKey, cancellationToken), asset.ContentType);
    }

    /// <summary>
    /// AI worker: stores an image produced for <paramref name="request"/> (authorized when the request was queued). The
    /// type and size come from the bytes; the image must suit the target platform before anything is written. Bytes go
    /// to storage first; the metadata row joins the caller's unit of work.
    /// </summary>
    public async Task<MediaAsset> StoreGeneratedAsync(AiGenerationRequest request, SocialPlatform platform, byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length > PlatformRules.MaxImageBytes)
            throw Guard.Invalid("Image", $"must be at most {PlatformRules.MaxImageBytes / (1024 * 1024)} MB");
        var image = ImageProbe.Inspect(bytes) ?? throw Guard.Invalid("Image", "returned by the provider is not a valid JPEG or PNG file");

        var id = Guid.NewGuid();
        var objectKey = $"organizations/{request.OrganizationId:N}/media/{id:N}{image.Extension}";
        var asset = MediaAsset.CreateGenerated(id, request.OrganizationId, image.ContentType, bytes.Length, image.Width, image.Height,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), objectKey, request.Id, request.Provider.ToString(), request.Model,
            request.RequestedByUserId, timeProvider.GetUtcNow().UtcDateTime);
        asset.EnsureSuitableFor(platform);

        await storage.SaveNewAsync(objectKey, bytes, asset.ContentType, cancellationToken);
        media.Add(asset);
        return asset;
    }

    /// <summary>Loads an asset that must belong to the authorized organization.</summary>
    internal async Task<MediaAsset> LoadAsync(OrganizationAccess access, Guid mediaAssetId, CancellationToken cancellationToken)
    {
        var asset = await media.GetAsync(mediaAssetId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Image not found.");
        access.EnsureOwns(asset.OrganizationId, "Image");
        return asset;
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBoundedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > PlatformRules.MaxImageBytes)
                throw Guard.Invalid("Image", $"must be at most {PlatformRules.MaxImageBytes / (1024 * 1024)} MB");
            buffer.Write(chunk, 0, read);
        }
        return buffer.Length > 0 ? buffer.ToArray() : throw Guard.Invalid("Image", "is empty");
    }
}
