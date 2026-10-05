using Microsoft.AspNetCore.DataProtection;
using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Infrastructure.Security;

/// <summary>
/// Encrypts tenant secrets with ASP.NET Core Data Protection. The API (which stores keys) and the worker (which uses
/// them) share one application name and key ring, so both can read the same protected values.
/// </summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("SocialMediaAutomation.TenantSecrets.v1");

    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => protector.Unprotect(protectedValue);
}
