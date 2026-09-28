namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Acquires heterogeneous typed tree requests in stable database order for one coordinated save.</summary>
internal static class NestedSetSaveLocks
{
    /// <summary>Locks the same immutable typed request objects collected from every affected save group.</summary>
    /// <param name="context">The context whose current transaction owns all acquired locks.</param>
    /// <param name="requests">The native views of persisted complete identities affected by the save.</param>
    /// <param name="cancellationToken">The token used for native ordering and lock acquisition.</param>
    /// <returns>A task that completes after every affected registry row is locked.</returns>
    internal static Task AcquireAsync(
        DbContext context,
        IReadOnlyList<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    ) => NestedSetTreeLocks.AcquireAsync(context, requests, cancellationToken);
}
