using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(CoreDbContext db) : IUserRepository
{
    public Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<User?> GetForUpdateAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public void Add(User user) => db.Users.Add(user);
}
