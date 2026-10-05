namespace SocialMediaAutomation.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>Commits all pending changes in one transaction. Throws CONCURRENCY_CONFLICT when a row version is stale.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Drops all pending, unsaved changes (used before recording a failure so partial work is never persisted).</summary>
    void DiscardChanges();
}
