namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Bounds native guard work independently of a large unchanged hierarchy tracker.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Exercises large tracker populations while reusing the complete native identity test models.</summary>
    [Collection("Scale scenarios")]
    public abstract partial class Scale : ProviderTest
    {
        private const int NativeCandidateCount = 20000;

        private readonly ModelCompatibilityDatabase _fixture;

        /// <summary>Uses isolated provider databases for actual guard and state checks.</summary>
        protected Scale(
            IProviderFixture<ModelCompatibilityDatabase> fixture
        ) : base(fixture)
        {
            _fixture = fixture.Value;
        }

        /// <summary>Uses one native key collection for 20,000 unrelated integer or Guid candidates.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DeleteNativeScaleAsync_NativeKeys_UseOneProbeForTwentyThousandUnrelatedCandidates(
            bool guidKeys
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateNativeScaleContextAsync(
                Engine,
                guidKeys,
                cancellationToken: TestContext.Current.CancellationToken,
                probe);

            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var affectedTree = Guid.NewGuid();
            var otherTree = Guid.NewGuid();
            await SeedNativeScaleAsync(
                context,
                guidKeys,
                affectedTree,
                root,
                1,
                cancellationToken: TestContext.Current.CancellationToken);

            await SeedNativeScaleAsync(
                context,
                guidKeys,
                otherTree,
                root + 100,
                NativeCandidateCount - 1,
                cancellationToken: TestContext.Current.CancellationToken);

            await TrackNativeScaleAsync(
                context,
                guidKeys,
                otherTree,
                cancellationToken: TestContext.Current.CancellationToken);

            probe.Armed = true;

            // Act
            var failure = await Record.ExceptionAsync(() => DeleteNativeScaleAsync(
                context,
                guidKeys,
                affectedTree,
                CancellationToken.None));

            // Assert
            Assert.Null(failure);
            Assert.Equal(1, probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 2);

            Assert.Equal(
                NativeCandidateCount,
                context
                    .ChangeTracker
                    .Entries()
                    .Count());

            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        }

        /// <summary>Converted NodeKeys fit one bounded scalar probe beyond the former 64-key limit.</summary>
        [Fact]
        public async Task DeleteTreeAsync_ConvertedKeys_UseOneNativeProbeForSixtyFiveCandidates()
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await _fixture.CreateContextAsync<ConvertedKeyGuardContext>(
                Engine,
                static options => new ConvertedKeyGuardContext(options),
                cancellationToken: TestContext.Current.CancellationToken,
                probe);

            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
            var affectedTree = $"guard-converted-{root}";
            var otherTree = $"guard-converted-other-{root}";
            await SeedTreeAsync(
                context,
                affectedTree,
                root,
                1,
                cancellationToken: TestContext.Current.CancellationToken);

            await SeedTreeAsync(
                context,
                otherTree,
                root + 100,
                64,
                cancellationToken: TestContext.Current.CancellationToken);

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
            Assert.All(
                tracked,
                node => Assert.Equal(
                    EntityState.Unchanged,
                    context.Entry(node)
                        .State));
        }

        /// <summary>Transports every native candidate once per bounded batch of requested trees.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteNativeRequestsAsync_NativeKeys_ProbeEachSixtyFourTreeRequestBatchOnce(
            bool guidKeys
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateNativeScaleContextAsync(
                Engine,
                guidKeys,
                cancellationToken: TestContext.Current.CancellationToken,
                probe);

            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var otherTree = Guid.NewGuid();
            var requestedTrees = Enumerable
                .Range(0, 65)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            await SeedNativeScaleAsync(
                context,
                guidKeys,
                otherTree,
                root + 100,
                NativeCandidateCount - 1,
                cancellationToken: TestContext.Current.CancellationToken);

            await SeedNativeRequestTreesAsync(
                context,
                guidKeys,
                requestedTrees,
                root,
                cancellationToken: TestContext.Current.CancellationToken);

            await TrackNativeScaleAsync(
                context,
                guidKeys,
                otherTree,
                cancellationToken: TestContext.Current.CancellationToken);

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

        /// <summary>Counts persisted affected rows without reusing the tracked stale identity.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static Task<int> CountNativeScaleTreeAsync(
            DbContext context,
            bool guidKeys,
            Guid treeId,
            CancellationToken cancellationToken
        )
        {
            if (guidKeys)
            {
                return context
                    .NestedSet<NativeGuidGuardNode>()
                    .InTree(treeId)
                    .Nodes
                    .CountAsync(cancellationToken);
            }

            return context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(treeId.ToString("N")))
                .Nodes
                .CountAsync(cancellationToken);
        }

        /// <summary>Uses disjoint deterministic Guid keys for repeatable setup without identity collisions.</summary>
        private static Guid NativeGuidKey(
            int value
        ) => new(value, 0, 0, new byte[8]);
    }
}
