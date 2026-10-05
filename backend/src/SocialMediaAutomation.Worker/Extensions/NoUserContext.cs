using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Worker.Extensions;

/// <summary>
/// Workers act on durable requests, not on behalf of a signed-in session. Any user-facing authorization path
/// therefore fails closed (UNAUTHORIZED) if it is ever reached from a worker.
/// </summary>
internal sealed class NoUserContext : ICurrentUser
{
    public Guid? UserId => null;
}

/// <summary>Worker work is not an API request; the correlation id travels on the request or job row itself.</summary>
internal sealed class NoCorrelationContext : ICorrelationContext
{
    public string? CorrelationId => null;
}
