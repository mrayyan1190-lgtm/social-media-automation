namespace SocialMediaAutomation.Application.Abstractions;

/// <summary>The authenticated caller as resolved by the host (API cookie, or none in workers).</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
