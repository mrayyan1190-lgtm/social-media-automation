using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Infrastructure.Persistence;

internal sealed class UnitOfWork(CoreDbContext db) : IUnitOfWork
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public void DiscardChanges() => db.ChangeTracker.Clear();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var newPosts = db.ChangeTracker.Entries<Post>()
                .Where(e => e.State == EntityState.Added && e.Entity.CurrentVersionId is not null)
                .Select(e => (Entry: e, CurrentVersionId: e.Entity.CurrentVersionId))
                .ToList();

            if (newPosts.Count == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            // A new Post and its Version 1 reference each other through composite FKs, which EF cannot order in one
            // batch. Insert the post with no current pointer, then the version, then set the pointer, all inside one
            // transaction, so a post without its current version is never visible.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var (entry, _) in newPosts)
                entry.Property(p => p.CurrentVersionId).CurrentValue = null;
            await db.SaveChangesAsync(cancellationToken);
            foreach (var (entry, currentVersionId) in newPosts)
                entry.Property(p => p.CurrentVersionId).CurrentValue = currentVersionId;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException(ErrorCodes.ConcurrencyConflict, "The resource was changed by someone else. Reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            // Two requests raced to create the same unique record (e.g. the workspace's single brand profile,
            // a second active post for one platform, or the same next version number).
            throw new BusinessException(ErrorCodes.ConcurrencyConflict, "A conflicting change was saved at the same time. Reload and try again.");
        }
    }
}
