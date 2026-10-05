using Microsoft.AspNetCore.Identity;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher, used without the Identity tables.</summary>
internal sealed class PasswordHasher : Application.Abstractions.IPasswordHasher
{
    private readonly PasswordHasher<User> inner = new();

    public string Hash(string password) => inner.HashPassword(null!, password);

    public bool Verify(string passwordHash, string password) =>
        inner.VerifyHashedPassword(null!, passwordHash, password) != PasswordVerificationResult.Failed;
}
