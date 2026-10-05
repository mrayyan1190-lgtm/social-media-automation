using SocialMediaAutomation.Domain.Common;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// Global sign-in identity. A User owns no business data and has no OrganizationId:
/// workspace access comes only from <see cref="OrganizationMember"/> rows.
/// </summary>
public sealed class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public bool IsActive { get; private set; }

    /// <summary>
    /// Platform-level operator (internal administration). Not an OrganizationMember role: it grants no workspace access, and
    /// no product endpoint can set it; it is assigned only by the development seed or directly in the database.
    /// </summary>
    public bool IsSystemAdmin { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static User Create(Guid id, string email, string displayName, string passwordHash, DateTime utcNow) => new()
    {
        Id = id,
        Email = email.Trim(),
        NormalizedEmail = NormalizeEmail(email),
        DisplayName = displayName.Trim(),
        PasswordHash = passwordHash,
        IsActive = true,
        CreatedAtUtc = utcNow,
    };

    public static User CreateSystemAdmin(Guid id, string email, string displayName, string passwordHash, DateTime utcNow)
    {
        var user = Create(id, email, displayName, passwordHash, utcNow);
        user.IsSystemAdmin = true;
        return user;
    }

    public const int DisplayNameMaxLength = 200;

    /// <summary>
    /// Self-serve sign-up asks only for email, password and workspace name; the name is entered on the onboarding profile step,
    /// so an empty DisplayName means that step is still open.
    /// </summary>
    public bool ProfileCompleted => DisplayName.Length > 0;

    public void CompleteProfile(string displayName) => DisplayName = Guard.Required(displayName, "Full name", DisplayNameMaxLength);

    /// <summary>Platform operators only. A disabled account cannot sign in, and its open sessions stop working.</summary>
    public void Disable() => IsActive = false;

    public void Enable() => IsActive = true;

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}
