namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Uses distinct models so large scalar keys cannot collide with small existing fixtures.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private async Task<DbContext> CreateScalarContextAsync(
            string engine,
            bool convertedKeys,
            CancellationToken cancellationToken,
            params IInterceptor[] interceptors
        )
        {
            if (convertedKeys)
            {
                return await _fixture.CreateContextAsync<ScalarConvertedGuardContext>(
                    engine,
                    static options => new ScalarConvertedGuardContext(options),
                    cancellationToken: cancellationToken,
                    interceptors);
            }

            return await _fixture.CreateContextAsync<ScalarStringGuardContext>(
                engine,
                static options => new ScalarStringGuardContext(options),
                cancellationToken: cancellationToken,
                interceptors);
        }

        /// <summary>Imports active request roots through the public forest API before tracking candidates.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static async Task SeedScalarRequestsAsync(
            DbContext context,
            bool convertedKeys,
            Guid[] treeIds,
            int firstKey,
            CancellationToken cancellationToken
        )
        {
            if (convertedKeys)
            {
                await SeedNativeRequestTreesAsync(
                    context,
                    false,
                    treeIds,
                    firstKey,
                    cancellationToken: cancellationToken);

                return;
            }

            var trees = treeIds
                .Select((treeId,
                    index) => new NestedSetTreeImport<ScalarStringNode, Guid>(
                    treeId,
                    new NestedSetBranch<ScalarStringNode>(
                        new ScalarStringNode
                        {
                            Id = ScalarKey(firstKey + index),
                            Name = "Root",
                        })))
                .ToArray();

            await context
                .NestedSet<ScalarStringNode>()
                .InsertForestAsync(trees, cancellationToken);

            context.ChangeTracker.Clear();
        }

        /// <summary>Imports real leaf candidates through the public API without direct SQL seeding.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static async Task SeedScalarTreeAsync(
            DbContext context,
            bool convertedKeys,
            Guid treeId,
            int root,
            int children,
            CancellationToken cancellationToken
        )
        {
            if (convertedKeys)
            {
                await SeedTreeAsync(
                    context,
                    treeId.ToString("N"),
                    root,
                    children,
                    cancellationToken: cancellationToken);

                return;
            }

            var leaves = Enumerable
                .Range(0, children)
                .Select(index => new NestedSetBranch<ScalarStringNode>(
                    new ScalarStringNode
                    {
                        Id = ScalarKey(root + index + 1),
                        Name = $"Child-{index:D5}",
                    }))
                .ToArray();

            var branch = new NestedSetBranch<ScalarStringNode>(
                new ScalarStringNode
                {
                    Id = ScalarKey(root),
                    Name = "Root",
                },
                leaves);

            await context
                .NestedSet<ScalarStringNode>()
                .InsertForestAsync(
                    [new NestedSetTreeImport<ScalarStringNode, Guid>(treeId, branch)],
                    cancellationToken);

            context.ChangeTracker.Clear();
        }

        /// <summary>Tracks only leaf candidates so a stale final entry represents exactly one persisted node.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static async Task TrackScalarChildrenAsync(
            DbContext context,
            bool convertedKeys,
            Guid treeId,
            CancellationToken cancellationToken
        )
        {
            if (convertedKeys)
            {
                var tree = new BroadTreeId(treeId.ToString("N"));
                _ = await context
                    .Set<BroadTreeIdNode>()
                    .Where(node => node.TreeId == tree && node.ParentId != null)
                    .OrderBy(node => node.Left)
                    .ToArrayAsync(cancellationToken);

                return;
            }

            _ = await context
                .Set<ScalarStringNode>()
                .Where(node => node.TreeId == treeId && node.ParentId != null)
                .OrderBy(node => node.Left)
                .ToArrayAsync(cancellationToken);
        }
    }
}
