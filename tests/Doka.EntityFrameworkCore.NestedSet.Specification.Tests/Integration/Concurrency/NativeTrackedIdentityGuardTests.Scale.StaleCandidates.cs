namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Finds an affected persisted row at the end of a large tracker despite its stale TreeId.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DeleteNativeScaleAsync_TwentyThousandUnrelatedEntries_RejectsAffectedStaleCandidate(
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
                var stale = await ReadNodeAsync(
                    context,
                    root + 1,
                    cancellationToken: TestContext.Current.CancellationToken);

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
            Assert.Equal(
                NestedSetErrorCode.InvalidContext,
                Assert.IsType<NestedSetException>(failure)
                    .Code);

            Assert.Equal(1, probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 2);
            Assert.Equal(
                NativeCandidateCount + 1,
                context
                    .ChangeTracker
                    .Entries()
                    .Count());

            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Equal(
                2,
                await CountNativeScaleTreeAsync(
                    context,
                    guidKeys,
                    affectedTree,
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        /// <summary>Rejects affected stale membership found only in the second batch of requested tree locks.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteNativeRequestsAsync_SecondTreeRequestBatch_RejectsAffectedStaleMembership(
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
                var stale = await ReadNodeAsync(
                    context,
                    root + 64,
                    cancellationToken: TestContext.Current.CancellationToken);

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
            Assert.Equal(
                NestedSetErrorCode.InvalidContext,
                Assert.IsType<NestedSetException>(failure)
                    .Code);

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
            Assert.Equal(
                1,
                await CountNativeScaleTreeAsync(
                    context,
                    guidKeys,
                    requestedTrees[0],
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(
                1,
                await CountNativeScaleTreeAsync(
                    context,
                    guidKeys,
                    requestedTrees[64],
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(
                NativeCandidateCount,
                await CountNativeScaleTreeAsync(
                    context,
                    guidKeys,
                    otherTree,
                    cancellationToken: TestContext.Current.CancellationToken));
        }
    }
}
