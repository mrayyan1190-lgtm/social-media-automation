using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tracked, for changes saved through the unit of work.</summary>
    Task<User?> GetForUpdateAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    void Add(User user);
}
