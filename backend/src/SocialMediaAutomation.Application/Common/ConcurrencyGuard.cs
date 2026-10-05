using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Common;

public static class ConcurrencyGuard
{
    /// <summary>
    /// Rejects a mutation based on a stale read. The database row-version check at save time
    /// still catches races between this check and the commit.
    /// </summary>
    public static void EnsureCurrent(byte[] currentRowVersion, byte[]? expectedRowVersion)
    {
        if (expectedRowVersion is null)
            throw new BusinessException(ErrorCodes.ValidationFailed, "The expected row version (If-Match) is required for this change.");
        if (!currentRowVersion.AsSpan().SequenceEqual(expectedRowVersion))
            throw new BusinessException(ErrorCodes.ConcurrencyConflict, "The resource was changed by someone else. Reload and try again.");
    }
}
