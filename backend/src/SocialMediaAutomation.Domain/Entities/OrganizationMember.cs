using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>Links a global <see cref="User"/> to one <see cref="Organization"/> with a workspace-specific role.</summary>
public sealed class OrganizationMember
{
    private OrganizationMember() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive => Status == MembershipStatus.Active;

    public static OrganizationMember Create(Guid id, Guid organizationId, Guid userId, OrganizationRole role, DateTime utcNow) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        UserId = userId,
        Role = ValidateRole(role),
        Status = MembershipStatus.Active,
        CreatedAtUtc = utcNow,
    };

    public bool Has(Permission permission) => IsActive && RolePermissions.Has(Role, permission);

    public void ChangeRole(OrganizationRole role) => Role = ValidateRole(role);

    /// <summary>Removes workspace access without deleting the row, so audit history and authored content keep their author.</summary>
    public void Deactivate() => Status = MembershipStatus.Suspended;

    public void Reactivate(OrganizationRole role)
    {
        Role = ValidateRole(role);
        Status = MembershipStatus.Active;
    }

    private static OrganizationRole ValidateRole(OrganizationRole role) =>
        Enum.IsDefined(role) ? role : throw new BusinessException(ErrorCodes.ValidationFailed, $"Unknown role {(int)role}.");
}
