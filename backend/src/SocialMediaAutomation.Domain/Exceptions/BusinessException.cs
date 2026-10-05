namespace SocialMediaAutomation.Domain.Exceptions;

/// <summary>A rule violation carrying a stable code from <see cref="Constants.ErrorCodes"/>; the API maps the code to an HTTP status.</summary>
public sealed class BusinessException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
