namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Bounds scalar NodeKey transport independently from tracker size and requested tree count.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Combines a large scalar tracker with both candidate and requested-tree batch boundaries.</summary>
        public static IEnumerable<TheoryDataRow<bool, int, int>> ScalarCollectionCases()
        {
            foreach (var converted in new[] { false, true })
            {
                yield return new TheoryDataRow<bool, int, int>(converted, 65, 1);
                yield return new TheoryDataRow<bool, int, int>(converted, 20000, 1);
                yield return new TheoryDataRow<bool, int, int>(converted, 769, 65);
            }
        }

        /// <summary>Native string and converted keys obey the combined scalar and tree-request probe ceiling.</summary>
        /// <param name="convertedKeys">Whether an explicit conversion requires balanced scalar equality.</param>
        /// <param name="candidates">The number of real unrelated tracked leaf nodes.</param>
        /// <param name="requests">The number of active tree identities protected by the operation.</param>
        [Theory]
        [MemberData(nameof(ScalarCollectionCases))]
        public async Task ScalarCandidatesRespectBothBatchBoundaries(
            bool convertedKeys,
            int candidates,
            int requests
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateScalarContextAsync(Engine, convertedKeys,
                cancellationToken: TestContext.Current.CancellationToken,
                probe);

            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var otherTree = Guid.NewGuid();
            var requestedTrees = Enumerable
                .Range(0, requests)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            await SeedScalarRequestsAsync(context, convertedKeys, requestedTrees, root,
                cancellationToken: TestContext.Current.CancellationToken);
            await SeedScalarTreeAsync(context, convertedKeys, otherTree, root + 100, candidates,
                cancellationToken: TestContext.Current.CancellationToken);
            await TrackScalarChildrenAsync(context, convertedKeys, otherTree,
                cancellationToken: TestContext.Current.CancellationToken);
            var before = ScalarSnapshots(context, convertedKeys);
            await using var baseline = await CreateScalarContextAsync(Engine, convertedKeys,
                cancellationToken: TestContext.Current.CancellationToken);

            await ExecuteScalarRequestsAsync(baseline, convertedKeys, requestedTrees, static _ => Task.CompletedTask,
                cancellationToken: TestContext.Current.CancellationToken);
            var baselineBefore = GC.GetTotalAllocatedBytes(precise: true);
            await ExecuteScalarRequestsAsync(baseline, convertedKeys, requestedTrees, static _ => Task.CompletedTask,
                cancellationToken: TestContext.Current.CancellationToken);
            var baselineAllocated = GC.GetTotalAllocatedBytes(precise: true) - baselineBefore;
            var calls = 0;
            probe.Armed = true;

            // Act
            var crowdedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var failure = await Record.ExceptionAsync(() => ExecuteScalarRequestsAsync(
                context,
                convertedKeys,
                requestedTrees,
                _ =>
                {
                    calls++;

                    return Task.CompletedTask;
                }, cancellationToken: TestContext.Current.CancellationToken));

            var crowdedAllocated = GC.GetTotalAllocatedBytes(precise: true) - crowdedBefore;

            // Assert
            _output.WriteLine(
                $"Scalar candidates={candidates}; converted={convertedKeys}; requests={requests}; "
                + $"baseline bytes={baselineAllocated}; crowded bytes={crowdedAllocated}; "
                + $"additional bytes per entry={(crowdedAllocated - baselineAllocated) / candidates}; "
                + $"native reads={probe.NativeReads}; native parameters={probe.MaximumNativeParameters}");
            Assert.Null(failure);
            Assert.Equal(1, calls);
            Assert.Equal((candidates + 767) / 768 * ((requests + 63) / 64), probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 1664);

            if (candidates == 65)
            {
                // WHY: Native fixed IN pads 65 keys to 70, while converted scalar OR uses all 65 exact parameters.
                // Both queries add one TreeId parameter; this distinguishes the transport paths observably.
                Assert.Equal(convertedKeys ? 66 : 71, probe.MaximumNativeParameters);
            }

            Assert.Equal(candidates, before.Length);
            Assert.Equal(before, ScalarSnapshots(context, convertedKeys));
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Equal(candidates + 1, await ScalarTreeCountAsync(context, convertedKeys, otherTree,
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(1, await ScalarTreeCountAsync(context, convertedKeys, requestedTrees[^1],
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(context.Database.CurrentTransaction);
        }

        /// <summary>An affected persisted leaf in the final candidate and request batches stops payload work.</summary>
        /// <param name="convertedKeys">Whether the key follows the explicit-conversion scalar fallback.</param>
        /// <param name="candidates">The size of the real tracked candidate set.</param>
        /// <param name="requests">The number of active requested trees.</param>
        [Theory]
        [MemberData(nameof(ScalarCollectionCases))]
        public async Task ScalarLastCandidateInLastRequestBatchRejectsStaleMembership(
            bool convertedKeys,
            int candidates,
            int requests
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateScalarContextAsync(Engine, convertedKeys,
                cancellationToken: TestContext.Current.CancellationToken,
                probe);

            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var otherTree = Guid.NewGuid();
            var requestedTrees = Enumerable
                .Range(0, requests)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            await SeedScalarRequestsAsync(context, convertedKeys, requestedTrees, root,
                cancellationToken: TestContext.Current.CancellationToken);
            await SeedScalarTreeAsync(context, convertedKeys, otherTree, root + 100, candidates,
                cancellationToken: TestContext.Current.CancellationToken);
            await TrackScalarChildrenAsync(context, convertedKeys, otherTree,
                cancellationToken: TestContext.Current.CancellationToken);
            var before = ScalarSnapshots(context, convertedKeys);
            await using var writer = await CreateScalarContextAsync(Engine, convertedKeys,
                cancellationToken: TestContext.Current.CancellationToken);

            // WHY: All candidates are leaves. Choosing the actual last tracker entry avoids depending on key or
            // attachment ordering; moving it through a separate context leaves this reader's TreeId genuinely stale.
            await MoveLastScalarCandidateAsync(context, writer, convertedKeys, root + requests - 1,
                cancellationToken: TestContext.Current.CancellationToken);
            var calls = 0;
            probe.Armed = true;

            // Act
            var failure = await Record.ExceptionAsync(() => ExecuteScalarRequestsAsync(
                context,
                convertedKeys,
                requestedTrees,
                _ =>
                {
                    calls++;

                    return Task.CompletedTask;
                }, cancellationToken: TestContext.Current.CancellationToken));

            // Assert
            Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
            Assert.Equal(0, calls);
            Assert.Equal((candidates + 767) / 768 * ((requests + 63) / 64), probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 1664);
            Assert.Equal(candidates, before.Length);
            Assert.Equal(before, ScalarSnapshots(context, convertedKeys));
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Equal(candidates, await ScalarTreeCountAsync(context, convertedKeys, otherTree,
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(2, await ScalarTreeCountAsync(context, convertedKeys, requestedTrees[^1],
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(context.Database.CurrentTransaction);
        }

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
                await SeedNativeRequestTreesAsync(context, false, treeIds, firstKey,
                    cancellationToken: cancellationToken);

                return;
            }

            var trees = treeIds
                .Select((
                    treeId,
                    index
                ) => new NestedSetTreeImport<ScalarStringNode, Guid>(
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
                await SeedTreeAsync(context, treeId.ToString("N"), root, children,
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

        /// <summary>Changes persisted membership through another public context while retaining snapshots.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static Task MoveLastScalarCandidateAsync(
            DbContext reader,
            DbContext writer,
            bool convertedKeys,
            int parent,
            CancellationToken cancellationToken
        )
        {
            if (convertedKeys)
            {
                var last = reader
                    .ChangeTracker
                    .Entries<BroadTreeIdNode>()
                    .Last()
                    .Entity;

                return writer
                    .NestedSet<BroadTreeIdNode>()
                    .MoveToAsync(last.Id, parent, cancellationToken);
            }

            var leaf = reader
                .ChangeTracker
                .Entries<ScalarStringNode>()
                .Last()
                .Entity;

            return writer
                .NestedSet<ScalarStringNode>()
                .MoveToAsync(leaf.Id, ScalarKey(parent), cancellationToken);
        }

        /// <summary>Runs exact typed requests without changing payload, isolating guard query budgets.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static Task ExecuteScalarRequestsAsync(
            DbContext context,
            bool convertedKeys,
            Guid[] trees,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken
        )
        {
            if (convertedKeys)
            {
                return ExecuteNativeRequestsAsync(context, false, trees, operation, cancellationToken);
            }

            var type = context.Model.FindEntityType(typeof(ScalarStringNode))!;
            var requests = trees
                .Select(tree => new NestedSetTreeLockRequest<Guid, NestedSetNoScope>(
                    type,
                    default,
                    tree,
                    NestedSetTreeLockMode.Existing))
                .ToArray();

            return new NestedSetMutationExecutor<ScalarStringNode, string, Guid, NestedSetNoScope>(context, type)
                .ExecuteAsync(operation, requests, cancellationToken);
        }

        /// <summary>Preserves every tracked identity, relationship, coordinate and ordinary payload value.</summary>
        private static ScalarState[] ScalarSnapshots(
            DbContext context,
            bool convertedKeys
        ) => (convertedKeys
                ? context
                    .ChangeTracker
                    .Entries<BroadTreeIdNode>()
                    .Select(entry => new ScalarState(
                        ScalarKey(entry.Entity.Id),
                        entry.Entity.TreeId.Value,
                        entry.Entity.ParentId is { } parent ? ScalarKey(parent) : null,
                        entry.Entity.Left,
                        entry.Entity.Right,
                        entry.Entity.Depth,
                        entry.Entity.Position,
                        entry.Entity.Name))
                : context
                    .ChangeTracker
                    .Entries<ScalarStringNode>()
                    .Select(entry => new ScalarState(
                        entry.Entity.Id,
                        entry.Entity.TreeId.ToString("N"),
                        entry.Entity.ParentId,
                        entry.Entity.Left,
                        entry.Entity.Right,
                        entry.Entity.Depth,
                        entry.Entity.Position,
                        entry.Entity.Name)))
            .OrderBy(state => state.Key, StringComparer.Ordinal)
            .ToArray();

        /// <summary>Reads persisted tree membership independently of the reader's stale tracked entities.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static Task<int> ScalarTreeCountAsync(
            DbContext context,
            bool convertedKeys,
            Guid treeId,
            CancellationToken cancellationToken
        ) => convertedKeys
            ? context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(treeId.ToString("N")))
                .Nodes
                .CountAsync(cancellationToken)
            : context
                .NestedSet<ScalarStringNode>()
                .InTree(treeId)
                .Nodes
                .CountAsync(cancellationToken);

        /// <summary>Creates non-overlapping fixed-width string identities with identical provider ordering.</summary>
        private static string ScalarKey(
            int key
        ) => $"KEY-{key:D10}";

        /// <summary>Retains exact immutable entity values across allowed and rejected explicit operations.</summary>
        private readonly record struct ScalarState(
            string Key,
            string TreeId,
            string? Parent,
            long Left,
            long Right,
            int Depth,
            long Position,
            string Name
        );

        /// <summary>Uses an explicit converted key mapping under an independently owned table identity.</summary>
        private sealed class ScalarConvertedGuardContext(DbContextOptions<ScalarConvertedGuardContext> options)
            : GuardTreeContext(options)
        {
            /// <inheritdoc />
            protected override bool CaseInsensitive => true;

            /// <inheritdoc />
            protected override bool BroadComparer => false;

            /// <inheritdoc />
            protected override void OnModelCreating(
                ModelBuilder modelBuilder
            )
            {
                base.OnModelCreating(modelBuilder);
                var node = modelBuilder.Entity<BroadTreeIdNode>();
                node
                    .Property(value => value.Id)
                    .HasConversion<string>()
                    .HasMaxLength(20);
                node
                    .Property(value => value.ParentId)
                    .HasConversion<string>()
                    .HasMaxLength(20);
            }
        }

        /// <summary>Maps native string keys with known ordinal collation and compatible nullable parents.</summary>
        private sealed class ScalarStringGuardContext(DbContextOptions<ScalarStringGuardContext> options)
            : DbContext(options)
        {
            /// <inheritdoc />
            protected override void OnModelCreating(
                ModelBuilder modelBuilder
            )
            {
                var collation = Database.IsSqlite()
                    ? "BINARY"
                    : Database.IsSqlServer()
                        ? "Latin1_General_100_BIN2"
                        : Database.IsNpgsql()
                            ? "C"
                            : "utf8mb4_bin";

                var node = modelBuilder.Entity<ScalarStringNode>();
                node.ToTable("ScalarStringGuardNodes");
                node.HasKey(value => value.Id);
                node
                    .Property(value => value.Id)
                    .HasMaxLength(40)
                    .UseCollation(collation)
                    .ValueGeneratedNever();
                node
                    .Property(value => value.ParentId)
                    .HasMaxLength(40)
                    .UseCollation(collation);
                node
                    .Property(value => value.Name)
                    .HasMaxLength(100);
                node
                    .HasOne<ScalarStringNode>()
                    .WithMany()
                    .HasForeignKey(value => value.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);
                node.HasNestedSet(nestedSet => nestedSet
                    .HasNodeKey(value => value.Id)
                    .HasTreeId(value => value.TreeId)
                    .HasParent(value => value.ParentId)
                    .HasBounds(value => value.Left, value => value.Right)
                    .HasDepth(value => value.Depth)
                    .HasPosition(value => value.Position));
            }
        }

        /// <summary>Supplies native scalar strings independently of the converted-key control model.</summary>
        private sealed class ScalarStringNode
        {
            /// <summary>Gets or sets the exact native string key.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the stable tree identity.</summary>
            public Guid TreeId { get; set; }

            /// <summary>Gets or sets the nullable direct-parent key.</summary>
            public string? ParentId { get; set; }

            /// <summary>Gets or sets the inclusive left boundary.</summary>
            public long Left { get; set; }

            /// <summary>Gets or sets the inclusive right boundary.</summary>
            public long Right { get; set; }

            /// <summary>Gets or sets the zero-based depth.</summary>
            public int Depth { get; set; }

            /// <summary>Gets or sets the zero-based sibling position.</summary>
            public long Position { get; set; }

            /// <summary>Gets or sets the ordinary payload value.</summary>
            public string Name { get; set; } = string.Empty;
        }
    }
}
