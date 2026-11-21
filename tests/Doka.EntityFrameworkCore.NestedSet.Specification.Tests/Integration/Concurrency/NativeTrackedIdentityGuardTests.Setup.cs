namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Creates the case-insensitive TreeId model with observers local to this operation.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private Task<AliasGuardTreeContext> CreateAliasContextAsync(
        string engine,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    ) => _fixture.CreateContextAsync<AliasGuardTreeContext>(
        engine,
        static options => new AliasGuardTreeContext(options),
        cancellationToken: cancellationToken,
        interceptors);

    /// <summary>Imports one root with independent keys and clears only arrangement-owned tracking.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SeedTreeAsync(
        DbContext context,
        string treeId,
        int root,
        int children,
        CancellationToken cancellationToken
    )
    {
        var branches = Enumerable
            .Range(0, children)
            .Select(index => new NestedSetBranch<BroadTreeIdNode>(
                new BroadTreeIdNode
                {
                    Id = root + index + 1,
                    Name = $"Child-{index:D3}",
                }))
            .ToArray();

        var branch = new NestedSetBranch<BroadTreeIdNode>(
            new BroadTreeIdNode
            {
                Id = root,
                Name = "Root",
            },
            branches);

        await context
            .NestedSet<BroadTreeIdNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<BroadTreeIdNode, BroadTreeId>(new BroadTreeId(treeId), branch)],
                cancellationToken);

        context.ChangeTracker.Clear();
    }

    /// <summary>Reads an independent entity snapshot without triggering identity-map alias handling.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<BroadTreeIdNode> ReadNodeAsync(
        DbContext context,
        int id,
        CancellationToken cancellationToken
    ) => context
        .Set<BroadTreeIdNode>()
        .AsNoTracking()
        .SingleAsync(node => node.Id == id, cancellationToken);
}
