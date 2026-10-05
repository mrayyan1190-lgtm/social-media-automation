using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Publishing;

public sealed record ProviderResponse(JsonNode? Body, HttpResponseHeaders Headers, string? RequestId)
{
    public string? Header(string name) => Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}

/// <summary>A non-success provider response, already parsed. Messages never include tokens.</summary>
public sealed class ProviderHttpException(HttpStatusCode status, string? providerCode, string message, string? requestId, JsonNode? body)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public string? ProviderCode { get; } = providerCode;
    public string? RequestId { get; } = requestId;
    public JsonNode? Body { get; } = body;
}

/// <summary>
/// Shared plumbing for live publishers: bearer-authenticated JSON calls and the default error normalization
/// (ARCHITECTURE.md §34). Subclasses add provider-specific error codes; the categories mean the same for every provider.
/// </summary>
public abstract class HttpPublisher(HttpClient http) : ISocialPublisher
{
    public const string HttpClientName = "social-publish";

    private static readonly string[] RequestIdHeaders = ["x-li-uuid", "x-li-request-id", "x-fb-request-id", "x-fb-trace-id", "x-request-id"];

    protected abstract SocialPlatform Platform { get; }

    public bool CanPublish(SocialPlatform platform, ExecutionMode mode) =>
        platform == Platform && mode is ExecutionMode.LIVE or ExecutionMode.TEST_ACCOUNT;

    public virtual bool UsesMediaContainer => false;

    public abstract Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken);

    public virtual Task<string> PrepareAsync(PublishContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{Platform} publishing has no preparation step.");

    public virtual Task<MediaContainerStatus> GetStatusAsync(PublishContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{Platform} publishing has no media container.");

    public abstract Task<PublishReceipt> PublishAsync(PublishContext context, CancellationToken cancellationToken);

    public PublishFailure NormalizeError(Exception error, bool mayHaveCreatedPost) => error switch
    {
        PublishFailure failure => failure,
        ProviderHttpException provider => NormalizeProvider(provider, mayHaveCreatedPost) ?? NormalizeStatus(provider, mayHaveCreatedPost),
        // The connection was never established, so the request cannot have reached the provider.
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError } =>
            new(PublishErrorCategory.RETRYABLE, "NETWORK_BEFORE_SEND", $"{Platform} could not be reached; nothing was sent."),
        HttpRequestException or TaskCanceledException or TimeoutException or IOException when mayHaveCreatedPost =>
            new(PublishErrorCategory.UNCERTAIN, "TIMEOUT_AFTER_SEND",
                $"The {Platform} request was sent but no answer arrived ({error.GetType().Name}); the post may exist. Check the destination."),
        HttpRequestException or TaskCanceledException or TimeoutException or IOException =>
            new(PublishErrorCategory.RETRYABLE, "NETWORK_ERROR", $"{Platform} did not answer ({error.GetType().Name})."),
        _ when mayHaveCreatedPost => new(PublishErrorCategory.UNCERTAIN, "UNREADABLE_RESPONSE",
            $"The {Platform} response could not be understood ({error.GetType().Name}); the post may exist."),
        _ => new(PublishErrorCategory.RETRYABLE, "UNEXPECTED_PROVIDER_ERROR", $"{Platform} call failed ({error.GetType().Name})."),
    };

    /// <summary>Provider-specific error codes (e.g. Meta's code 190 for an expired token). Return null to use the HTTP status.</summary>
    protected virtual PublishFailure? NormalizeProvider(ProviderHttpException error, bool mayHaveCreatedPost) => null;

    private PublishFailure NormalizeStatus(ProviderHttpException error, bool mayHaveCreatedPost)
    {
        var detail = $"{Platform}: {error.Message}";
        return (int)error.Status switch
        {
            401 => new(PublishErrorCategory.ACTION_REQUIRED, "AUTH_INVALID", $"{detail}. Reconnect the destination.", error.RequestId),
            403 => new(PublishErrorCategory.ACTION_REQUIRED, "PERMISSION_MISSING", $"{detail}. The connection lacks a required permission.", error.RequestId),
            404 => new(PublishErrorCategory.ACTION_REQUIRED, "ACCOUNT_INVALID", $"{detail}. The destination account was not found.", error.RequestId),
            // Throttled requests are rejected before processing, so nothing was created.
            429 => new(PublishErrorCategory.RETRYABLE, "THROTTLED", $"{detail}. Rate limited; retrying later.", error.RequestId),
            >= 500 when mayHaveCreatedPost => new(PublishErrorCategory.UNCERTAIN, "PROVIDER_ERROR_AFTER_SEND",
                $"{detail}. The provider failed while handling the publish request; the post may exist.", error.RequestId),
            >= 500 => new(PublishErrorCategory.RETRYABLE, "PROVIDER_UNAVAILABLE", detail, error.RequestId),
            _ => new(PublishErrorCategory.ACTION_REQUIRED, "PROVIDER_REJECTED", $"{detail}. Correct the content or destination.", error.RequestId),
        };
    }

    // ---- HTTP helpers -------------------------------------------------------------------------------------

    /// <summary>Sends and parses JSON; any failure surfaces as the normalized category for this kind of call.</summary>
    protected async Task<ProviderResponse> SendAsync(
        HttpRequestMessage request, string accessToken, bool mayCreatePost, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonNode? body = null;
            if (text.Length > 0)
            {
                try
                {
                    body = JsonNode.Parse(text);
                }
                catch (JsonException) when (!response.IsSuccessStatusCode)
                {
                    // Error pages are not always JSON; the status code still classifies the failure.
                }
            }
            var requestId = RequestIdHeaders.Select(h => response.Headers.TryGetValues(h, out var v) ? v.FirstOrDefault() : null).FirstOrDefault(v => v is not null);
            if (!response.IsSuccessStatusCode)
                throw new ProviderHttpException(response.StatusCode, ProviderCode(body), ProviderMessage(body, response.StatusCode), requestId, body);
            return new ProviderResponse(body, response.Headers, requestId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw NormalizeError(ex, mayCreatePost);
        }
    }

    /// <summary>Reads the approved image before any create call; a storage failure here means nothing was sent.</summary>
    protected static async Task<Stream> OpenMediaAsync(PublishMedia media, CancellationToken cancellationToken)
    {
        try
        {
            return await media.OpenAsync(cancellationToken);
        }
        catch (IOException ex)
        {
            throw new PublishFailure(PublishErrorCategory.RETRYABLE, "MEDIA_READ_FAILED", $"The approved image could not be read ({ex.GetType().Name}).");
        }
    }

    protected static PublishFailure NotCapable(string code, string message) => new(PublishErrorCategory.ACTION_REQUIRED, code, message);

    protected static void RequireCapability(PublishContext context, string capability, string platformLabel)
    {
        if (!context.Capabilities.Contains(capability))
            throw NotCapable("CAPABILITY_MISSING", $"The {platformLabel} destination was not granted '{capability}'. Reconnect it with the required permission.");
        if (string.IsNullOrEmpty(context.AccessToken) || string.IsNullOrEmpty(context.ExternalAccountId))
            throw NotCapable("ACCOUNT_INVALID", $"The {platformLabel} destination has no usable connection.");
    }

    private static string? ProviderCode(JsonNode? body) =>
        body?["error"]?["code"]?.ToString() ?? body?["serviceErrorCode"]?.ToString() ?? body?["code"]?.ToString();

    private static string ProviderMessage(JsonNode? body, HttpStatusCode status)
    {
        var message = body?["error"]?["message"]?.ToString() ?? body?["message"]?.ToString() ?? $"HTTP {(int)status}";
        return message.Length > 300 ? message[..300] : message;
    }
}
