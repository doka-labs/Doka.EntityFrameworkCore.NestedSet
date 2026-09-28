namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Represents one stable TreeId selected from a configured nested-set hierarchy.</summary>
/// <typeparam name="TEntity">The configured hierarchy entity.</typeparam>
/// <typeparam name="TTreeId">The mapped TreeId type.</typeparam>
public sealed class NestedSetTree<TEntity, TTreeId>
    where TEntity : class
    where TTreeId : notnull
{
    private readonly NestedSetMutationBinding<TEntity> _operations;
    private readonly TTreeId _treeId;

    /// <summary>Creates a tree facade from its already validated identity query.</summary>
    /// <param name="nodes">The complete tree query in stable preorder.</param>
    /// <param name="operations">The validated operations binding for this hierarchy.</param>
    /// <param name="treeId">The owned snapshot already shared with the identity query.</param>
    internal NestedSetTree(
        IQueryable<TEntity> nodes,
        NestedSetMutationBinding<TEntity> operations,
        TTreeId treeId
    )
    {
        Nodes = nodes;
        _operations = operations;
        // WHY: InTree already created one immutable identity for both the deferred query and maintenance.
        // Cloning again would duplicate configured snapshot work without adding an ownership boundary.
        _treeId = treeId;
    }

    /// <summary>Gets all visible nodes in stable nested-set preorder.</summary>
    /// <value>A composable no-tracking query; call EF Core <c>AsTracking()</c> to opt into tracking.</value>
    public IQueryable<TEntity> Nodes { get; }

    /// <summary>Validates structural invariants of this exact tree.</summary>
    /// <param name="level">The validation detail required by the caller.</param>
    /// <param name="cancellationToken">The token used for snapshot reads and structural traversal.</param>
    /// <returns>A stable report containing tree-wide and node-specific issues.</returns>
    public Task<NestedSetValidationReport> ValidateAsync(
        NestedSetValidationLevel level,
        CancellationToken cancellationToken = default
    ) => _operations.ValidateTreeAsync(_treeId, level, cancellationToken);

    /// <summary>Computes the deterministic rebuild work without modifying the database.</summary>
    /// <param name="cancellationToken">The token used for snapshot reads and plan construction.</param>
    /// <returns>The repairability, bounded batch count, and structural roles that would change.</returns>
    public Task<NestedSetRebuildPlan> PlanRebuildAsync(
        CancellationToken cancellationToken = default
    ) => _operations.PlanRebuildAsync(_treeId, cancellationToken);

    /// <summary>Reconstructs bounds, depth, and position from valid stored adjacency and sibling order.</summary>
    /// <param name="cancellationToken">The token used for lock acquisition, traversal, and repair batches.</param>
    /// <returns>A task that completes after the atomic rebuild boundary.</returns>
    public Task RebuildAsync(
        CancellationToken cancellationToken = default
    ) => _operations.RebuildTreeAsync(_treeId, cancellationToken);
}
