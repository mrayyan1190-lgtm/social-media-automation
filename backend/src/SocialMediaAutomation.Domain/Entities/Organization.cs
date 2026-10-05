using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>The workspace and tenant boundary: every tenant-owned record carries this Id as OrganizationId.</summary>
public sealed class Organization
{
    public const int NameMaxLength = 200;
    public const int IndustryMaxLength = 200;
    public const string DefaultLanguageTag = "en";

    private Organization() { }

    public Guid Id { get; private set; }

    /// <summary>The business/workspace name; also the brand name the AI writes for.</summary>
    public string Name { get; private set; } = null!;

    public string Industry { get; private set; } = null!;
    public string DefaultLanguage { get; private set; } = null!;
    public string TimeZoneId { get; private set; } = null!;
    public bool RequireDistinctReviewer { get; private set; }
    public OrganizationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Organization Create(
        Guid id, string name, string industry, string? defaultLanguage, string timeZoneId, bool requireDistinctReviewer, DateTime utcNow)
    {
        var organization = new Organization { Id = id, Status = OrganizationStatus.Active, CreatedAtUtc = utcNow };
        organization.UpdateSettings(name, industry, defaultLanguage, timeZoneId, requireDistinctReviewer, utcNow);
        return organization;
    }

    public void UpdateSettings(string name, string industry, string? defaultLanguage, string timeZoneId, bool requireDistinctReviewer, DateTime utcNow)
    {
        Name = Guard.Required(name, "Workspace name", NameMaxLength);
        // Optional in the domain: self-serve sign-up does not ask for it; workspace settings can add it later.
        Industry = Guard.Optional(industry, "Industry", IndustryMaxLength) ?? "";
        DefaultLanguage = Guard.LanguageTag(string.IsNullOrWhiteSpace(defaultLanguage) ? DefaultLanguageTag : defaultLanguage, "Default language");
        TimeZoneId = ValidateIanaTimeZone(timeZoneId);
        RequireDistinctReviewer = requireDistinctReviewer;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Platform operators only. Members lose access and due posts are not sent; nothing is deleted.</summary>
    public void Suspend(DateTime utcNow)
    {
        Status = OrganizationStatus.Suspended;
        UpdatedAtUtc = utcNow;
    }

    public void Reactivate(DateTime utcNow)
    {
        Status = OrganizationStatus.Active;
        UpdatedAtUtc = utcNow;
    }

    // Scheduling resolves local time against this zone (PRD §25), so only IANA ids are accepted: never Windows ids or
    // fixed offsets such as "UTC+5", which cannot follow daylight-saving changes.
    private static string ValidateIanaTimeZone(string timeZoneId)
    {
        var trimmed = timeZoneId?.Trim() ?? "";
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(trimmed, out _)
            ? trimmed
            : throw new BusinessException(ErrorCodes.InvalidTimeZone,
                $"Time zone '{timeZoneId}' is not an IANA time zone id (for example 'Asia/Karachi' or 'Europe/Berlin').");
    }
}
