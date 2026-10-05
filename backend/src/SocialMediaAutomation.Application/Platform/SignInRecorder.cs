using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Platform;

public sealed record SignInContext(string? IpAddress, string? UserAgent);

/// <summary>
/// Writes sign-in, failed sign-in and sign-out events (customer and internal). Only the email, outcome and client hints are
/// kept; never the password or the session cookie. A logging failure never blocks the sign-in itself.
/// </summary>
public sealed class SignInRecorder(IPlatformRepository platform, IUnitOfWork unitOfWork, ICorrelationContext correlation, ISystemEventLog systemLog, TimeProvider timeProvider)
{
    public const string CustomerArea = "CUSTOMER";
    public const string InternalArea = "INTERNAL";

    public async Task RecordAsync(Guid? userId, string email, string area, string eventType, SignInContext client, CancellationToken cancellationToken)
    {
        try
        {
            platform.Add(SignInEvent.Record(userId, email, area, eventType, client.IpAddress, client.UserAgent, correlation.CorrelationId,
                timeProvider.GetUtcNow().UtcDateTime));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await systemLog.RecordAsync(SystemEventLevels.Error, "API", "SIGN_IN_LOG_FAILED", ex.Message, correlation.CorrelationId, cancellationToken);
        }
    }
}
