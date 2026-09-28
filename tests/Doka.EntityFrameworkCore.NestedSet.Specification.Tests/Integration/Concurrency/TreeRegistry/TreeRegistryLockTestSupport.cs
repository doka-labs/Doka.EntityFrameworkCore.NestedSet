namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares registry request execution without declaring provider test cases.</summary>
internal static class TreeRegistryLockTestSupport
{
    /// <summary>Executes one complete identity through the production transaction boundary.</summary>
    internal static Task AcquireAsync(
        TreeContext context,
        int scope,
        Guid treeId,
        NestedSetTreeLockMode mode,
        CancellationToken cancellationToken
    ) => AcquireManyAsync(context, [Request(context, scope, treeId, mode)], cancellationToken);

    /// <summary>Runs multiple lock requests in one owned transaction.</summary>
    internal static Task AcquireManyAsync(
        TreeContext context,
        IReadOnlyList<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    ) => NestedSetTransaction.ExecuteAsync(
        context,
        token => NestedSetTreeLocks.AcquireAsync(context, requests, token),
        cancellationToken);

    /// <summary>Creates a complete TreeNode registry request.</summary>
    internal static NestedSetTreeLockRequest<Guid, int> Request(
        TreeContext context,
        int scope,
        Guid treeId,
        NestedSetTreeLockMode mode
    ) => new(context.Model.FindEntityType(typeof(TreeNode))!, scope, treeId, mode);

    /// <summary>Reads the persisted revision for an exact typed tree identity.</summary>
    internal static Task<long> ReadRevisionAsync(
        TreeContext context,
        int scope,
        Guid treeId
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(TreeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy)
            .Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Where(row => EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope) == scope
                && EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == treeId)
            .Select(row => EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision))
            .SingleAsync(CancellationToken.None);
    }

    /// <summary>Acquires the requested trees after both competing workers are ready.</summary>
    internal static async Task LockAfterGateAsync(
        TestDatabase database,
        int scope,
        Guid[] treeIds,
        Task gate,
        CancellationToken cancellationToken
    )
    {
        await gate;
        await using var context = database.CreateContext();
        var requests = treeIds
            .Select(treeId => Request(context, scope, treeId, NestedSetTreeLockMode.Existing))
            .ToArray();

        await AcquireManyAsync(context, requests, cancellationToken);
    }
}
