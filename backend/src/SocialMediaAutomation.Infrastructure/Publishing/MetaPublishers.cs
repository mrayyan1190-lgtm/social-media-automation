using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using SocialMediaAutomation.Application.Abstractions;
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
/// Facebook Page publishing: text/link via /feed or a single photo via /photos, using a Page token fetched with the
/// connected user's token right before the call (the fetch creates nothing).
/// </summary>
public sealed class FacebookPublisher(HttpClient http, IConfiguration configuration) : MetaPublisher(http, configuration)
{
    protected override SocialPlatform Platform => SocialPlatform.Facebook;

    public override Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken)
    {
        RequireCapability(context, context.Media is null ? "publish_text" : "publish_image", "Facebook Page");
        return Task.CompletedTask;
    }

    public override async Task<PublishReceipt> PublishAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var page = context.ExternalAccountId!;
        var pageToken = (await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Graph}/{page}?fields=access_token"), context.AccessToken!,
                mayCreatePost: false, cancellationToken)).Body?["access_token"]?.GetValue<string>()
            ?? throw NotCapable("PERMISSION_MISSING", "Facebook did not issue a Page token; the connection cannot post to this Page.");

        ProviderResponse created;
        if (context.Media is { } media)
        {
            await using var bytes = await OpenMediaAsync(media, cancellationToken);
            var image = new StreamContent(bytes);
            image.Headers.ContentType = new MediaTypeHeaderValue(media.ContentType);
            var form = new MultipartFormDataContent
            {
                { new StringContent(PublicationText.Compose(context.Payload, includeUrl: true)), "caption" },
                { image, "source", media.ContentType == "image/png" ? "image.png" : "image.jpg" },
            };
            created = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Graph}/{page}/photos") { Content = form }, pageToken, mayCreatePost: true, cancellationToken);
        }
        else
        {
            var form = Form(("message", PublicationText.Compose(context.Payload, includeUrl: false)), ("link", context.Payload.DestinationUrl));
            created = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Graph}/{page}/feed") { Content = form }, pageToken, mayCreatePost: true, cancellationToken);
        }

        var id = created.Body?["post_id"]?.GetValue<string>() ?? created.Body?["id"]?.GetValue<string>();
        return new PublishReceipt(id, id is null ? null : $"https://www.facebook.com/{id}", created.RequestId,
            context.Media is null ? "Facebook Page post created." : "Facebook Page photo post created.");
    }
}

/// <summary>
/// Instagram professional account publishing (single image + caption) as a durable multi-step flow: create a media
/// container from a public image URL (invisible), poll its status_code across worker runs, then media_publish.
/// </summary>
public sealed class InstagramPublisher(HttpClient http, IConfiguration configuration) : MetaPublisher(http, configuration)
{
    protected override SocialPlatform Platform => SocialPlatform.Instagram;

    public override bool UsesMediaContainer => true;

    public override Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken)
    {
        RequireCapability(context, "publish_image", "Instagram");
        if (context.Media is null)
            throw NotCapable("MEDIA_MISSING", "Instagram posts require one image.");
        // Instagram downloads the image itself, so it needs a public URL; private local storage cannot provide one.
        if (context.Media.PublicUrl is null)
            throw NotCapable("MEDIA_URL_UNAVAILABLE",
                "Instagram fetches the image from a public URL, and the configured media storage does not provide one. Configure public object storage.");
        return Task.CompletedTask;
    }

    public override async Task<string> PrepareAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var form = Form(("image_url", context.Media!.PublicUrl), ("caption", PublicationText.Compose(context.Payload, includeUrl: true)),
            ("alt_text", context.Media.AltText));
        // A container is not visible to anyone, so even an unanswered request here is safe to repeat.
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
