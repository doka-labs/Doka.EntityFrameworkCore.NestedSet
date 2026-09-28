namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Bounds native guard work independently of a large unchanged hierarchy tracker.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Isolates allocation measurements while reusing the complete native identity test models.</summary>
    [Collection("Allocation measurements")]
    public abstract partial class Scale : ProviderTest
    {
        private readonly ModelCompatibilityDatabase _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Uses isolated provider databases and retains measured allocation evidence.</summary>
        protected Scale(
            IProviderFixture<ModelCompatibilityDatabase> fixture,
            ITestOutputHelper output
        ) : base(fixture)
        {
            _fixture = fixture.Value;
            _output = output;
        }

        private const int NativeCandidateCount = 20000;

        /// <summary>Bounds tracker protection and native transport allocations beyond the warm baseline.</summary>
        private const long MaximumAdditionalBytesPerCandidate = 2600;

        /// <summary>Uses one native key collection for 20,000 unrelated integer or Guid candidates.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NativeKeysUseOneProbeForTwentyThousandUnrelatedCandidates(
            bool guidKeys
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateNativeScaleContextAsync(Engine, guidKeys, probe);
            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var affectedTree = Guid.NewGuid();
            var otherTree = Guid.NewGuid();
            await SeedNativeScaleAsync(context, guidKeys, affectedTree, root, 1);
            await SeedNativeScaleAsync(context, guidKeys, otherTree, root + 100, NativeCandidateCount - 1);
            await TrackNativeScaleAsync(context, guidKeys, otherTree);
            await using var baseline = await CreateNativeScaleContextAsync(Engine, guidKeys);
            var warmTree = Guid.NewGuid();
            var baselineTree = Guid.NewGuid();
            var baselineOtherTree = Guid.NewGuid();
            await SeedNativeScaleAsync(baseline, guidKeys, warmTree, root - 10, 0);
            await SeedNativeScaleAsync(baseline, guidKeys, baselineTree, root - 20, 0);
            await SeedNativeScaleAsync(baseline, guidKeys, baselineOtherTree, root - 30, 0);
            await TrackNativeScaleAsync(baseline, guidKeys, baselineOtherTree);
            await DeleteNativeScaleAsync(baseline, guidKeys, warmTree, CancellationToken.None);
            var baselineBefore = GC.GetTotalAllocatedBytes(precise: true);
            await DeleteNativeScaleAsync(baseline, guidKeys, baselineTree, CancellationToken.None);
            var baselineAllocated = GC.GetTotalAllocatedBytes(precise: true) - baselineBefore;
            probe.Armed = true;

            // Act
            var crowdedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var failure = await Record.ExceptionAsync(() => DeleteNativeScaleAsync(
                context,
                guidKeys,
                affectedTree,
                CancellationToken.None));

            var crowdedAllocated = GC.GetTotalAllocatedBytes(precise: true) - crowdedBefore;

            // Assert
            _output.WriteLine(
                $"Native candidates={NativeCandidateCount}; Guid keys={guidKeys}; "
                + $"baseline bytes={baselineAllocated}; crowded bytes={crowdedAllocated}; "
                + $"additional bytes per entry={(crowdedAllocated - baselineAllocated) / NativeCandidateCount}; "
                + $"native reads={probe.NativeReads}");
            Assert.Null(failure);
            Assert.Equal(1, probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 2);

            // WHY: Fresh five-provider int/Guid runs with fixed 64-candidate blocks measured at most
            // 2,214 additional bytes per entry. A 2,600-byte ceiling retains about 17% allocation headroom;
            // this budget checks memory growth and does not claim a CPU improvement.
            Assert.InRange(
                crowdedAllocated - baselineAllocated,
                0,
                NativeCandidateCount * MaximumAdditionalBytesPerCandidate);

            Assert.Equal(
                NativeCandidateCount,
                context
                    .ChangeTracker
                    .Entries()
                    .Count());
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        }

        /// <summary>Finds an affected persisted row at the end of a large tracker despite its stale TreeId.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NativeKeysRejectAnAffectedStaleCandidateAfterTwentyThousandUnrelatedEntries(
            bool guidKeys
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateNativeScaleContextAsync(Engine, guidKeys, probe);
            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var affectedTree = Guid.NewGuid();
            var otherTree = Guid.NewGuid();
            await SeedNativeScaleAsync(context, guidKeys, affectedTree, root, 1);
            await SeedNativeScaleAsync(context, guidKeys, otherTree, root + 100, NativeCandidateCount - 1);
            await TrackNativeScaleAsync(context, guidKeys, otherTree);

            if (guidKeys)
            {
                var id = NativeGuidKey(root + 1);
                var stale = await context
                    .Set<NativeGuidGuardNode>()
                    .AsNoTracking()
                    .SingleAsync(node => node.Id == id, CancellationToken.None);

                stale.TreeId = otherTree;
                context.Attach(stale);
            }
            else
            {
                var stale = await ReadNodeAsync(context, root + 1);
                stale.TreeId = new BroadTreeId(otherTree.ToString("N"));
                context.Attach(stale);
            }

            probe.Armed = true;

            // Act
            var failure = await Record.ExceptionAsync(() => DeleteNativeScaleAsync(
                context,
                guidKeys,
                affectedTree,
                CancellationToken.None));

            // Assert
            Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
            Assert.Equal(1, probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 2);
            Assert.Equal(
                NativeCandidateCount + 1,
                context
                    .ChangeTracker
                    .Entries()
                    .Count());
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Equal(2, await CountNativeScaleTreeAsync(context, guidKeys, affectedTree));
        }

        /// <summary>Converted NodeKeys fit one bounded scalar probe beyond the former 64-key limit.</summary>
        [Fact]
        public async Task ConvertedKeysUseOneNativeProbeForSixtyFiveCandidates()
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await _fixture.CreateContextAsync<ConvertedKeyGuardContext>(
                Engine,
                static options => new ConvertedKeyGuardContext(options),
                probe);

            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
            var affectedTree = $"guard-converted-{root}";
            var otherTree = $"guard-converted-other-{root}";
            await SeedTreeAsync(context, affectedTree, root, 1);
            await SeedTreeAsync(context, otherTree, root + 100, 64);
            var otherId = new BroadTreeId(otherTree);
            var tracked = await context
                .Set<BroadTreeIdNode>()
                .Where(node => node.TreeId == otherId)
                .ToArrayAsync(CancellationToken.None);

            probe.Armed = true;

            // Act
            var failure = await Record.ExceptionAsync(() => context
                .NestedSet<BroadTreeIdNode>()
                .DeleteTreeAsync(new BroadTreeId(affectedTree), CancellationToken.None));

            // Assert
            Assert.Null(failure);
            Assert.Equal(65, tracked.Length);
            Assert.Equal(1, probe.NativeReads);

            // WHY: The explicit conversion retains scalar OR: 65 distinct key parameters plus one TreeId.
            Assert.Equal(66, probe.MaximumNativeParameters);
            Assert.All(tracked, node => Assert.Equal(EntityState.Unchanged, context.Entry(node).State));
        }

        /// <summary>Transports every native candidate once per bounded batch of requested trees.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NativeKeysProbeEachSixtyFourTreeRequestBatchOnce(
            bool guidKeys
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateNativeScaleContextAsync(Engine, guidKeys, probe);
            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var otherTree = Guid.NewGuid();
            var requestedTrees = Enumerable
                .Range(0, 65)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            await SeedNativeScaleAsync(context, guidKeys, otherTree, root + 100, NativeCandidateCount - 1);
            await SeedNativeRequestTreesAsync(context, guidKeys, requestedTrees, root);
            await TrackNativeScaleAsync(context, guidKeys, otherTree);
            probe.Armed = true;

            // Act
            var failure = await Record.ExceptionAsync(() => ExecuteNativeRequestsAsync(
                context,
                guidKeys,
                requestedTrees,
                static _ => Task.CompletedTask,
                CancellationToken.None));

            // Assert
            Assert.Null(failure);
            Assert.Equal(2, probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 65);
            Assert.Equal(
                NativeCandidateCount,
                context
                    .ChangeTracker
                    .Entries()
                    .Count());
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        }

        /// <summary>Rejects affected stale membership found only in the second batch of requested tree locks.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SecondTreeRequestBatchRejectsAffectedStaleMembership(
            bool guidKeys
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateNativeScaleContextAsync(Engine, guidKeys, probe);
            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var otherTree = Guid.NewGuid();
            var requestedTrees = Enumerable
                .Range(0, 65)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            await SeedNativeScaleAsync(context, guidKeys, otherTree, root + 100, NativeCandidateCount - 1);
            await SeedNativeRequestTreesAsync(context, guidKeys, requestedTrees, root);
            await TrackNativeScaleAsync(context, guidKeys, otherTree);

            if (guidKeys)
            {
                var key = NativeGuidKey(root + 64);
                var stale = await context
                    .Set<NativeGuidGuardNode>()
                    .AsNoTracking()
                    .SingleAsync(node => node.Id == key, CancellationToken.None);

                stale.TreeId = otherTree;
                context.Attach(stale);
            }
            else
            {
                var stale = await ReadNodeAsync(context, root + 64);
                stale.TreeId = new BroadTreeId(otherTree.ToString("N"));
                context.Attach(stale);
            }

            var operationCalls = 0;
            probe.Armed = true;

            // Act
            var failure = await Record.ExceptionAsync(() => ExecuteNativeRequestsAsync(
                context,
                guidKeys,
                requestedTrees,
                _ =>
                {
                    operationCalls++;

                    return Task.CompletedTask;
                },
                CancellationToken.None));

            // Assert
            Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
            Assert.Equal(2, probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 65);
            Assert.Equal(0, operationCalls);
            Assert.Equal(
                NativeCandidateCount + 1,
                context
                    .ChangeTracker
                    .Entries()
                    .Count());
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Equal(1, await CountNativeScaleTreeAsync(context, guidKeys, requestedTrees[0]));
            Assert.Equal(1, await CountNativeScaleTreeAsync(context, guidKeys, requestedTrees[64]));
            Assert.Equal(NativeCandidateCount, await CountNativeScaleTreeAsync(context, guidKeys, otherTree));
        }

        /// <summary>Reserves the independent requested trees before arranging the large tracker.</summary>
        private static async Task SeedNativeRequestTreesAsync(
            DbContext context,
            bool guidKeys,
            Guid[] treeIds,
            int firstKey
        )
        {
            if (guidKeys)
            {
                var trees = treeIds
                    .Select((
                        treeId,
                        index
                    ) => new NestedSetTreeImport<NativeGuidGuardNode, Guid>(
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
                    .InsertForestAsync(trees, CancellationToken.None);
            }
            else
            {
                var trees = treeIds
                    .Select((
                        treeId,
                        index
                    ) => new NestedSetTreeImport<BroadTreeIdNode, BroadTreeId>(
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
                    .InsertForestAsync(trees, CancellationToken.None);
            }

            context.ChangeTracker.Clear();
        }

        /// <summary>Runs an observed operation through the exact typed executor with 65 active tree locks.</summary>
        private static Task ExecuteNativeRequestsAsync(
            DbContext context,
            bool guidKeys,
            Guid[] treeIds,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken
        )
        {
            if (guidKeys)
            {
                var entityType = context.Model.FindEntityType(typeof(NativeGuidGuardNode))!;
                var requests = treeIds
                    .Select(treeId => new NestedSetTreeLockRequest<Guid, NestedSetNoScope>(
                        entityType,
                        default,
                        treeId,
                        NestedSetTreeLockMode.Existing))
                    .ToArray();

                return new NestedSetMutationExecutor<NativeGuidGuardNode, Guid, Guid, NestedSetNoScope>(
                    context,
                    entityType).ExecuteAsync(operation, requests, cancellationToken);
            }

            var convertedEntity = context.Model.FindEntityType(typeof(BroadTreeIdNode))!;
            var convertedRequests = treeIds
                .Select(treeId => new NestedSetTreeLockRequest<BroadTreeId, NestedSetNoScope>(
                    convertedEntity,
                    default,
                    new BroadTreeId(treeId.ToString("N")),
                    NestedSetTreeLockMode.Existing))
                .ToArray();

            return new NestedSetMutationExecutor<BroadTreeIdNode, int, BroadTreeId, NestedSetNoScope>(
                context,
                convertedEntity).ExecuteAsync(operation, convertedRequests, cancellationToken);
        }

        /// <summary>Creates only the concrete key model requested by the provider theory.</summary>
        private async Task<DbContext> CreateNativeScaleContextAsync(
            string engine,
            bool guidKeys,
            params IInterceptor[] interceptors
        )
        {
            if (guidKeys)
            {
                return await _fixture.CreateContextAsync<NativeGuidGuardContext>(
                    engine,
                    static options => new NativeGuidGuardContext(options),
                    interceptors);
            }

            return await _fixture.CreateContextAsync<AliasGuardTreeContext>(
                engine,
                static options => new AliasGuardTreeContext(options),
                interceptors);
        }

        /// <summary>Imports native-key candidates without repeated per-node facade operations.</summary>
        private static async Task SeedNativeScaleAsync(
            DbContext context,
            bool guidKeys,
            Guid treeId,
            int root,
            int children
        )
        {
            if (!guidKeys)
            {
                await SeedTreeAsync(context, treeId.ToString("N"), root, children);

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
                    CancellationToken.None);

            context.ChangeTracker.Clear();
        }

        /// <summary>Tracks the complete unrelated tree in provider-defined row order.</summary>
        private static async Task TrackNativeScaleAsync(
            DbContext context,
            bool guidKeys,
            Guid treeId
        )
        {
            if (guidKeys)
            {
                _ = await context
                    .Set<NativeGuidGuardNode>()
                    .Where(node => node.TreeId == treeId)
                    .OrderBy(node => node.Left)
                    .ToArrayAsync(CancellationToken.None);

                return;
            }

            var converted = new BroadTreeId(treeId.ToString("N"));
            _ = await context
                .Set<BroadTreeIdNode>()
                .Where(node => node.TreeId == converted)
                .OrderBy(node => node.Left)
                .ToArrayAsync(CancellationToken.None);
        }

        /// <summary>Deletes through the public hierarchy API with the model's exact TreeId type.</summary>
        private static Task DeleteNativeScaleAsync(
            DbContext context,
            bool guidKeys,
            Guid treeId,
            CancellationToken cancellationToken
        ) => guidKeys
            ? context
                .NestedSet<NativeGuidGuardNode>()
                .DeleteTreeAsync(treeId, cancellationToken)
            : context
                .NestedSet<BroadTreeIdNode>()
                .DeleteTreeAsync(new BroadTreeId(treeId.ToString("N")), cancellationToken);

        /// <summary>Counts persisted affected rows without reusing the tracked stale identity.</summary>
        private static Task<int> CountNativeScaleTreeAsync(
            DbContext context,
            bool guidKeys,
            Guid treeId
        )
        {
            if (guidKeys)
            {
                return context
                    .NestedSet<NativeGuidGuardNode>()
                    .InTree(treeId)
                    .Nodes
                    .CountAsync(CancellationToken.None);
            }

            return context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(treeId.ToString("N")))
                .Nodes
                .CountAsync(CancellationToken.None);
        }

        /// <summary>Uses disjoint deterministic Guid keys for repeatable setup without identity collisions.</summary>
        private static Guid NativeGuidKey(
            int value
        ) => new(value, 0, 0, new byte[8]);

        /// <summary>Maps native Guid keys independently of the converted TreeId control.</summary>
        private sealed class NativeGuidGuardContext(DbContextOptions<NativeGuidGuardContext> options)
            : DbContext(options)
        {
            /// <inheritdoc />
            protected override void OnModelCreating(
                ModelBuilder modelBuilder
            )
            {
                var node = modelBuilder.Entity<NativeGuidGuardNode>();
                node.ToTable("NativeGuidGuardNodes");
                node.HasKey(value => value.Id);
                node
                    .Property(value => value.Id)
                    .ValueGeneratedNever();
                node
                    .Property(value => value.Name)
                    .HasMaxLength(100);
                node
                    .HasOne<NativeGuidGuardNode>()
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

        /// <summary>Stores converted NodeKeys to exercise the bounded scalar fallback.</summary>
        private sealed class ConvertedKeyGuardContext(DbContextOptions<ConvertedKeyGuardContext> options)
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

        /// <summary>Provides exact native Guid identities and ordinary mutable hierarchy metadata.</summary>
        private sealed class NativeGuidGuardNode
        {
            /// <summary>Gets or sets the native node key.</summary>
            public Guid Id { get; set; }

            /// <summary>Gets or sets the stable tree identity.</summary>
            public Guid TreeId { get; set; }

            /// <summary>Gets or sets the immediate parent key.</summary>
            public Guid? ParentId { get; set; }

            /// <summary>Gets or sets the left traversal bound.</summary>
            public long Left { get; set; }

            /// <summary>Gets or sets the right traversal bound.</summary>
            public long Right { get; set; }

            /// <summary>Gets or sets the root-relative depth.</summary>
            public int Depth { get; set; }

            /// <summary>Gets or sets the sibling position.</summary>
            public long Position { get; set; }

            /// <summary>Gets or sets the ordinary application payload.</summary>
            public string Name { get; set; } = string.Empty;
        }
    }
}
