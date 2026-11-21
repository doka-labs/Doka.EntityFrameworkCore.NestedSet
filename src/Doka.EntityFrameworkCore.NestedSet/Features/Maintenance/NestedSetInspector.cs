namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Inspects ordered adjacency and derives coordinates without database access.</summary>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
internal static partial class NestedSetInspector<TKey>
    where TKey : notnull
{
    /// <summary>Compares persisted coordinates with canonical adjacency and sibling order.</summary>
    /// <param name="nodes">The structural snapshot, with one row per primary key.</param>
    /// <param name="parents">Child-to-parent links resolved using the database's primary-key comparison rules.</param>
    /// <param name="keyComparer">The mapped key's equality semantics, including structural binary-key equality.</param>
    /// <param name="collectRepairs">Whether changed rows should be retained for a subsequent rebuild.</param>
    /// <param name="cancellationToken">The token checked while grouping and traversing the tree.</param>
    /// <param name="collectIssues">Whether node-specific diagnostics should be retained.</param>
    /// <param name="ordered">Whether database ordering already defines each sibling group's intended order.</param>
    /// <param name="issueLimit">Maximum retained issue samples; every violation is still counted.</param>
    /// <returns>Validation errors and, when requested, the changed derived coordinates.</returns>
    /// <exception cref="OverflowException">
    ///     The tree needs more boundaries than a 64-bit coordinate can represent.
    /// </exception>
    /// <exception cref="OperationCanceledException">The cancellation token is canceled.</exception>
    internal static NestedSetInspection<TKey> Inspect(
        NestedSetNode<TKey>[] nodes,
        Dictionary<TKey, TKey>? parents,
        IEqualityComparer<TKey> keyComparer,
        bool collectRepairs,
        bool ordered,
        bool collectIssues,
        int issueLimit,
        CancellationToken cancellationToken
    )
    {
        _ = checked((long)nodes.Length * 2);

        // WHY: The value context shares the existing buffers without adding one retained owner per inspection.
        var inspection = new InspectionRun(nodes, parents, keyComparer, collectIssues, issueLimit);
        inspection.Group(ordered, cancellationToken);
        inspection.ValidateOrder(ordered, cancellationToken);

        var (repairs, differences) = inspection.Walk(collectRepairs, ordered, cancellationToken);

        return inspection.Complete(repairs, differences, ordered);
    }
}
