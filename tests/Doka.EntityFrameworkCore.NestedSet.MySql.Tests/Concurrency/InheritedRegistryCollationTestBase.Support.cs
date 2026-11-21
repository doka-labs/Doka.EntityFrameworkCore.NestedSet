namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class InheritedRegistryCollationTestBase
{
    /// <summary>Creates complete typed requests for the production multi-tree locking regression seam.</summary>
    private static NestedSetTreeLockRequest<Guid, BroadScope>[] Requests(
        BroadScopeContext context,
        IReadOnlyList<BroadScope> scopes,
        Guid treeId,
        NestedSetTreeLockMode mode
    )
    {
        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;

        return scopes
            .Select(scope => new NestedSetTreeLockRequest<Guid, BroadScope>(
                entityType,
                scope,
                treeId,
                mode))
            .ToArray();
    }

    /// <summary>Exercises native deduplication and acquisition inside the normal transaction owner.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="requests">The complete typed tree-lock requests.</param>
    private static Task AcquireAsync(
        BroadScopeContext context,
        IReadOnlyList<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    ) => NestedSetTransaction.ExecuteAsync(
        context,
        token => NestedSetTreeLocks.AcquireAsync(context, requests, token),
        cancellationToken);

    /// <summary>Discards arrangement-only observations without altering database or tracked state.</summary>
    private static void ClearCaptures(
        EnterpriseProbe probe
    )
    {
        probe.Commands.Clear();
        probe.ParameterValues.Clear();
        probe.ParameterNames.Clear();
    }

    /// <summary>Returns Scope bindings from actual registry lifecycle locks, excluding rowset parameters.</summary>
    private static string[] LockScopes(
        EnterpriseProbe probe
    )
        => probe
            .Commands
            .Zip(probe.ParameterValues)
            .Where(command => command.First.Contains(
                NestedSetTreeRegistryMetadata.Lifecycle,
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(command => command.Second)
            .OfType<string>()
            .ToArray();

    /// <summary>Counts only the test's fresh TreeId across the model's own physical registry.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="treeId">The hierarchy tree identity.</param>
    private static Task<int> RegistryCountAsync(
        BroadScopeContext context,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy)
            .Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Where(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == treeId)
            .CountAsync(cancellationToken);
    }
}
