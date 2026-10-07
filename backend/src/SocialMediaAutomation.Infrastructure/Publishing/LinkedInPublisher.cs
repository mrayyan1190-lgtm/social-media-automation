using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.OAuth;

namespace SocialMediaAutomation.Infrastructure.Publishing;

/// <summary>
/// LinkedIn Posts API (versioned REST, "Share on LinkedIn"): text with an optional single image, posted as the
/// connected member. The image is uploaded first (initializeUpload + PUT), which creates nothing visible; only the final
/// POST /rest/posts can create the post.
/// </summary>
public sealed class LinkedInPublisher(HttpClient http, IConfiguration configuration) : HttpPublisher(http)
{
    public const string ApiBase = "https://api.linkedin.com/rest";
    /// <summary>
    /// LinkedIn retires each monthly API version about a year after release, so a fixed default expires on its own
    /// (202509 stopped working in 2026). Default to the version from three months ago: released and stable, and well
    /// inside the support window. SocialProviders:LinkedIn:ApiVersion still pins an exact version when set.
    /// </summary>
    public static string DefaultApiVersion => DateTime.UtcNow.AddMonths(-3).ToString("yyyyMM");

    // Reserved characters of LinkedIn's "little text" commentary format must be escaped to appear literally.
    private const string Reserved = "\\|{}@[]()<>#*_~";

    protected override SocialPlatform Platform => SocialPlatform.LinkedIn;

    private string ApiVersion => ProviderApp.From(configuration, SocialPlatform.LinkedIn).ApiVersion is { Length: > 0 } v ? v : DefaultApiVersion;

    public override Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken)
    {
        RequireCapability(context, context.Media is null ? "publish_text" : "publish_image", "LinkedIn");
        if (!context.ExternalAccountId!.StartsWith("urn:li:person:", StringComparison.Ordinal)
            && !context.ExternalAccountId.StartsWith("urn:li:organization:", StringComparison.Ordinal))
            throw NotCapable("ACCOUNT_INVALID", "The LinkedIn destination is not a member or organization URN.");
        return Task.CompletedTask;
    }

    public override async Task<PublishReceipt> PublishAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var token = context.AccessToken!;
        string? imageUrn = null;
        if (context.Media is { } media)
        {
            var init = await SendAsync(Rest(HttpMethod.Post, "/images?action=initializeUpload",
                new { initializeUploadRequest = new { owner = context.ExternalAccountId } }), token, mayCreatePost: false, cancellationToken);
            var uploadUrl = init.Body?["value"]?["uploadUrl"]?.GetValue<string>();
            imageUrn = init.Body?["value"]?["image"]?.GetValue<string>();
            if (uploadUrl is null || imageUrn is null)
                throw NotCapable("PROVIDER_REJECTED", "LinkedIn did not return an image upload URL.");
            await using var bytes = await OpenMediaAsync(media, cancellationToken);
            var upload = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new StreamContent(bytes) };
            await SendAsync(upload, token, mayCreatePost: false, cancellationToken);
        }

        var post = new JsonObject
        {
            ["author"] = context.ExternalAccountId,
            ["commentary"] = PublicationText.Compose(context.Payload, includeUrl: true, Escape, tag => $"{{hashtag|\\#|{tag}}}"),
            ["visibility"] = "PUBLIC",
            ["distribution"] = new JsonObject
            {
                ["feedDistribution"] = "MAIN_FEED", ["targetEntities"] = new JsonArray(), ["thirdPartyDistributionChannels"] = new JsonArray(),
            },
            ["lifecycleState"] = "PUBLISHED",
            ["isReshareDisabledByAuthor"] = false,
        };
        if (imageUrn is not null)
            post["content"] = new JsonObject { ["media"] = new JsonObject { ["id"] = imageUrn, ["altText"] = context.Media!.AltText ?? "" } };

        var created = await SendAsync(Rest(HttpMethod.Post, "/posts", post), token, mayCreatePost: true, cancellationToken);
        var urn = created.Header("x-restli-id") ?? created.Header("x-linkedin-id");
        return new PublishReceipt(urn, urn is null ? null : $"https://www.linkedin.com/feed/update/{urn}/", created.RequestId,
            $"LinkedIn post created{(imageUrn is null ? "" : " with 1 image")}.");
    }

    public static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Reserved.Contains(c))
                builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    private HttpRequestMessage Rest(HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, ApiBase + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("LinkedIn-Version", ApiVersion);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        return request;
    }
}
