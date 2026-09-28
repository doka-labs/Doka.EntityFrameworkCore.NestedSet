namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares test-free native-identity arrangement and nonoverlapping assigned key ranges.</summary>
internal static class NativeTrackedIdentityGuardTestSupport
{
    private static int s_nextRoot = 100000;

    /// <summary>Reserves one key range across shared and provider-local compatibility-table consumers.</summary>
    /// <param name="spacing">The original scenario's complete reserved key-range width.</param>
    /// <returns>The first assigned root key in the independently reserved range.</returns>
    internal static int NextRoot(
        int spacing
    ) =>
        // WHY: Shared and provider-local suites still write the same compatibility model tables. A single
        // allocator prevents the physical source move from introducing overlapping assigned key ranges.
        Interlocked.Add(ref s_nextRoot, spacing);

    /// <summary>Seeds one scoped root and child before attaching any native aliases.</summary>
    /// <param name="context">The exact compatibility context arranged by the scenario.</param>
    /// <param name="scope">The stored Scope identity.</param>
    /// <param name="treeId">The newly allocated tree identity.</param>
    /// <param name="root">The assigned first key in the centrally reserved range.</param>
    /// <returns>A task completing after import and arrangement-owned tracker cleanup.</returns>
    internal static async Task SeedScopedNativeAsync(
        DbContext context,
        string scope,
        Guid treeId,
        int root
    )
    {
        var branch = new NestedSetBranch<ScopeAliasNode>(
            new ScopeAliasNode
            {
                Id = root,
                Name = "Root",
            },
            [
                new NestedSetBranch<ScopeAliasNode>(
                    new ScopeAliasNode
                    {
                        Id = root + 1,
                        Name = "Child",
                    }),
            ]);

        await context
            .NestedSet<ScopeAliasNode>()
            .ForScope(scope)
            .InsertForestAsync([new NestedSetTreeImport<ScopeAliasNode, Guid>(treeId, branch)], CancellationToken.None);

        context.ChangeTracker.Clear();
    }

    /// <summary>Seeds identical NodeKeys in a provider-distinct converted Scope.</summary>
    /// <param name="context">The exact compatibility context arranged by the scenario.</param>
    /// <param name="scope">The converted Scope value with its native persisted comparison.</param>
    /// <param name="treeId">The newly allocated tree identity.</param>
    /// <param name="root">The assigned first key in the centrally reserved range.</param>
    /// <returns>A task completing after import and arrangement-owned tracker cleanup.</returns>
    internal static async Task SeedBroadScopeAsync(
        DbContext context,
        BroadScope scope,
        Guid treeId,
        int root
    )
    {
        var branch = new NestedSetBranch<BroadScopeNode>(
            new BroadScopeNode
            {
                Id = root,
                Name = "Root",
            },
            [
                new NestedSetBranch<BroadScopeNode>(
                    new BroadScopeNode
                    {
                        Id = root + 1,
                        Name = "Child",
                    }),
            ]);

        await context
            .NestedSet<BroadScopeNode>()
            .ForScope(scope)
            .InsertForestAsync([new NestedSetTreeImport<BroadScopeNode, Guid>(treeId, branch)], CancellationToken.None);

        context.ChangeTracker.Clear();
    }
}
