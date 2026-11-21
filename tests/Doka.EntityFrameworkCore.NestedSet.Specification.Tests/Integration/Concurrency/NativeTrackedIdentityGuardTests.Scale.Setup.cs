namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Reserves the independent requested trees before arranging the large tracker.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static async Task SeedNativeRequestTreesAsync(
            DbContext context,
            bool guidKeys,
            Guid[] treeIds,
            int firstKey,
            CancellationToken cancellationToken
        )
        {
            if (guidKeys)
            {
                var trees = treeIds
                    .Select((treeId,
                        index) => new NestedSetTreeImport<NativeGuidGuardNode, Guid>(
                        treeId,
                        new NestedSetBranch<NativeGuidGuardNode>(
                            new NativeGuidGuardNode
                            {
                                Id = NativeGuidKey(firstKey + index),
                                Name = "Root",
                            })))
                    .ToArray();

                await context
                    .NestedSet<NativeGuidGuardNode>()
                    .InsertForestAsync(trees, cancellationToken);
            }
            else
            {
                var trees = treeIds
                    .Select((treeId,
                        index) => new NestedSetTreeImport<BroadTreeIdNode, BroadTreeId>(
                        new BroadTreeId(treeId.ToString("N")),
                        new NestedSetBranch<BroadTreeIdNode>(
                            new BroadTreeIdNode
                            {
                                Id = firstKey + index,
                                Name = "Root",
                            })))
                    .ToArray();

                await context
                    .NestedSet<BroadTreeIdNode>()
                    .InsertForestAsync(trees, cancellationToken);
            }

            context.ChangeTracker.Clear();
        }

        /// <summary>Creates only the concrete key model requested by the provider theory.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private async Task<DbContext> CreateNativeScaleContextAsync(
            string engine,
            bool guidKeys,
            CancellationToken cancellationToken,
            params IInterceptor[] interceptors
        )
        {
            if (guidKeys)
            {
                return await _fixture.CreateContextAsync<NativeGuidGuardContext>(
                    engine,
                    static options => new NativeGuidGuardContext(options),
                    cancellationToken: cancellationToken,
                    interceptors);
            }

            return await _fixture.CreateContextAsync<AliasGuardTreeContext>(
                engine,
                static options => new AliasGuardTreeContext(options),
                cancellationToken: cancellationToken,
                interceptors);
        }

        /// <summary>Imports native-key candidates without repeated per-node facade operations.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static async Task SeedNativeScaleAsync(
            DbContext context,
            bool guidKeys,
            Guid treeId,
            int root,
            int children,
            CancellationToken cancellationToken
        )
        {
            if (!guidKeys)
            {
                await SeedTreeAsync(
                    context,
                    treeId.ToString("N"),
                    root,
                    children,
                    cancellationToken: cancellationToken);

                return;
            }

            var branches = Enumerable
                .Range(0, children)
                .Select(index => new NestedSetBranch<NativeGuidGuardNode>(
                    new NativeGuidGuardNode
                    {
                        Id = NativeGuidKey(root + index + 1),
                        Name = $"Child-{index:D5}",
                    }))
                .ToArray();

            var branch = new NestedSetBranch<NativeGuidGuardNode>(
                new NativeGuidGuardNode
                {
                    Id = NativeGuidKey(root),
                    Name = "Root",
                },
                branches);

            await context
                .NestedSet<NativeGuidGuardNode>()
                .InsertForestAsync(
                    [new NestedSetTreeImport<NativeGuidGuardNode, Guid>(treeId, branch)],
                    cancellationToken);

            context.ChangeTracker.Clear();
        }

        /// <summary>Tracks the complete unrelated tree in provider-defined row order.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static async Task TrackNativeScaleAsync(
            DbContext context,
            bool guidKeys,
            Guid treeId,
            CancellationToken cancellationToken
        )
        {
            if (guidKeys)
            {
                _ = await context
                    .Set<NativeGuidGuardNode>()
                    .Where(node => node.TreeId == treeId)
                    .OrderBy(node => node.Left)
                    .ToArrayAsync(cancellationToken);

                return;
            }

            var converted = new BroadTreeId(treeId.ToString("N"));
            _ = await context
                .Set<BroadTreeIdNode>()
                .Where(node => node.TreeId == converted)
                .OrderBy(node => node.Left)
                .ToArrayAsync(cancellationToken);
        }
    }
}
