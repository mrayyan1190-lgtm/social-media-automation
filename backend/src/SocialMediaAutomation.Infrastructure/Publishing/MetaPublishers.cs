using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.OAuth;

namespace SocialMediaAutomation.Infrastructure.Publishing;

/// <summary>Graph API error codes shared by Facebook and Instagram publishing.</summary>
public abstract class MetaPublisher(HttpClient http, IConfiguration configuration) : HttpPublisher(http)
{
    public const string DefaultGraphVersion = "v21.0";

    protected string Graph => $"https://graph.facebook.com/{(ProviderApp.From(configuration, Platform).ApiVersion is { Length: > 0 } v ? v : DefaultGraphVersion)}";

    protected override PublishFailure? NormalizeProvider(ProviderHttpException error, bool mayHaveCreatedPost) => error.ProviderCode switch
    {
        // 190: access token expired or invalidated.
        "190" or "102" => new(PublishErrorCategory.ACTION_REQUIRED, "AUTH_EXPIRED", $"{Platform}: {error.Message}. Reconnect the destination.", error.RequestId),
        // 4, 17, 32, 613: documented rate limits; the call was rejected without being processed.
        "4" or "17" or "32" or "613" => new(PublishErrorCategory.RETRYABLE, "THROTTLED", $"{Platform}: {error.Message}. Rate limited; retrying later.", error.RequestId),
        // 10 and 200-299: permission errors.
        "10" or "200" or "210" or "220" or "230" => new(PublishErrorCategory.ACTION_REQUIRED, "PERMISSION_MISSING",
            $"{Platform}: {error.Message}. The connection lacks a required permission.", error.RequestId),
        _ => null,
    };

    protected static FormUrlEncodedContent Form(params (string Key, string? Value)[] fields) =>
        new(fields.Where(f => f.Value is not null).Select(f => new KeyValuePair<string, string>(f.Key, f.Value!)));
}

/// <summary>
/// Facebook Page publishing: text/link via /feed, a single photo via /photos, or a multi-photo post (photos uploaded
/// unpublished, then one /feed post with attached_media), using a Page token fetched with the connected user's token
/// right before the call (the fetch creates nothing).
/// </summary>
public sealed class FacebookPublisher(HttpClient http, IConfiguration configuration) : MetaPublisher(http, configuration)
{
    protected override SocialPlatform Platform => SocialPlatform.Facebook;

    public override Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken)
    {
        RequireCapability(context, context.Media.Count == 0 ? "publish_text" : "publish_image", "Facebook Page");
        return Task.CompletedTask;
    }

    public override async Task<PublishReceipt> PublishAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var page = context.ExternalAccountId!;
        // A destination connected with a pasted Page token already holds the Page token; OAuth ones hold a user token.
        var pageToken = context.Capabilities.Contains(PageTokenConnectionService.PageTokenCapability)
            ? context.AccessToken!
            : (await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Graph}/{page}?fields=access_token"), context.AccessToken!,
                mayCreatePost: false, cancellationToken)).Body?["access_token"]?.GetValue<string>()
            ?? throw NotCapable("PERMISSION_MISSING", "Facebook did not issue a Page token; the connection cannot post to this Page.");

        ProviderResponse created;
        if (context.Media.Count == 1)
        {
            using var form = PhotoForm(context.Media[0], await OpenMediaAsync(context.Media[0], cancellationToken),
                ("caption", PublicationText.Compose(context.Payload, includeUrl: true)));
            created = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Graph}/{page}/photos") { Content = form }, pageToken, mayCreatePost: true, cancellationToken);
        }
        else if (context.Media.Count > 1)
        {
            // Unpublished photos are invisible until the feed post attaches them, so these uploads cannot create a post.
            var fields = new List<(string, string?)> { ("message", PublicationText.Compose(context.Payload, includeUrl: true)) };
            for (var i = 0; i < context.Media.Count; i++)
            {
                using var form = PhotoForm(context.Media[i], await OpenMediaAsync(context.Media[i], cancellationToken), ("published", "false"));
                var photo = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Graph}/{page}/photos") { Content = form }, pageToken, mayCreatePost: false, cancellationToken);
                var photoId = photo.Body?["id"]?.GetValue<string>() ?? throw NotCapable("PROVIDER_REJECTED", "Facebook did not return a photo id.");
                fields.Add(($"attached_media[{i}]", $"{{\"media_fbid\":\"{photoId}\"}}"));
            }
            created = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Graph}/{page}/feed") { Content = Form([.. fields]) }, pageToken, mayCreatePost: true, cancellationToken);
        }
        else
        {
            var form = Form(("message", PublicationText.Compose(context.Payload, includeUrl: false)), ("link", context.Payload.DestinationUrl));
            created = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Graph}/{page}/feed") { Content = form }, pageToken, mayCreatePost: true, cancellationToken);
        }

        var id = created.Body?["post_id"]?.GetValue<string>() ?? created.Body?["id"]?.GetValue<string>();
        return new PublishReceipt(id, id is null ? null : $"https://www.facebook.com/{id}", created.RequestId,
            context.Media.Count switch { 0 => "Facebook Page post created.", 1 => "Facebook Page photo post created.", var n => $"Facebook Page post with {n} photos created." });
    }

    /// <summary>A multipart photo upload; the form owns (and disposes) the image stream.</summary>
    private static MultipartFormDataContent PhotoForm(PublishMedia media, Stream bytes, (string Key, string Value) field)
    {
        var image = new StreamContent(bytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(media.ContentType);
        return new MultipartFormDataContent
        {
            { new StringContent(field.Value), field.Key },
            { image, "source", media.ContentType == "image/png" ? "image.png" : "image.jpg" },
        };
    }
}

/// <summary>
/// Instagram professional account publishing (single image or a 2-4 image carousel, with caption) as a durable
/// multi-step flow: create a media container from public image URLs (a carousel is one item container per image plus
/// the carousel container; all invisible), poll its status_code across worker runs, then media_publish.
/// </summary>
public sealed class InstagramPublisher(HttpClient http, IConfiguration configuration) : MetaPublisher(http, configuration)
{
    protected override SocialPlatform Platform => SocialPlatform.Instagram;

    public override bool UsesMediaContainer => true;

    public override Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken)
    {
        RequireCapability(context, "publish_image", "Instagram");
        if (context.Media.Count == 0)
            throw NotCapable("MEDIA_MISSING", "Instagram posts require at least one image.");
        // Instagram downloads the image itself, so it needs a public URL; private local storage cannot provide one.
        if (context.Media.Any(m => m.PublicUrl is null))
            throw NotCapable("MEDIA_URL_UNAVAILABLE",
                "Instagram fetches the image from a public URL, and the configured media storage does not provide one. Configure public object storage.");
        return Task.CompletedTask;
    }

    public override async Task<string> PrepareAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var caption = PublicationText.Compose(context.Payload, includeUrl: true);
        // Containers are not visible to anyone, so even an unanswered request here is safe to repeat. Only the returned
        // (carousel) container id is checkpointed; item containers are recreated if the worker restarts before that.
        if (context.Media.Count == 1)
            return await CreateContainerAsync(context, Form(("image_url", context.Media[0].PublicUrl), ("caption", caption), ("alt_text", context.Media[0].AltText)), cancellationToken);

        var children = new List<string>();
        foreach (var media in context.Media)
            children.Add(await CreateContainerAsync(context, Form(("image_url", media.PublicUrl), ("is_carousel_item", "true"), ("alt_text", media.AltText)), cancellationToken));
        return await CreateContainerAsync(context, Form(("media_type", "CAROUSEL"), ("children", string.Join(',', children)), ("caption", caption)), cancellationToken);
    }

    private async Task<string> CreateContainerAsync(PublishContext context, FormUrlEncodedContent form, CancellationToken cancellationToken)
    {
        var created = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Graph}/{context.ExternalAccountId}/media") { Content = form },
            context.AccessToken!, mayCreatePost: false, cancellationToken);
        return created.Body?["id"]?.GetValue<string>() ?? throw NotCapable("PROVIDER_REJECTED", "Instagram did not return a media container id.");
    }

    public override async Task<MediaContainerStatus> GetStatusAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var status = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Graph}/{context.ContainerId}?fields=status_code"), context.AccessToken!,
            mayCreatePost: false, cancellationToken);
        return status.Body?["status_code"]?.GetValue<string>() switch
        {
            "FINISHED" => MediaContainerStatus.Ready,
            "ERROR" or "EXPIRED" => MediaContainerStatus.Failed,
            _ => MediaContainerStatus.InProgress,
        };
    }

    public override async Task<PublishReceipt> PublishAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var published = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Graph}/{context.ExternalAccountId}/media_publish")
        {
            Content = Form(("creation_id", context.ContainerId)),
        }, context.AccessToken!, mayCreatePost: true, cancellationToken);
        var mediaId = published.Body?["id"]?.GetValue<string>();

        // Best effort only: the post exists now, so a failed permalink lookup must not turn success into an error.
        string? permalink = null;
        if (mediaId is not null)
        {
            try
            {
                permalink = (await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Graph}/{mediaId}?fields=permalink"), context.AccessToken!,
                    mayCreatePost: false, cancellationToken)).Body?["permalink"]?.GetValue<string>();
            }
            catch (PublishFailure)
            {
                // keep the id without a URL
            }
        }
        return new PublishReceipt(mediaId, permalink, published.RequestId, $"Instagram media published from container {context.ContainerId}.");
    }
}
