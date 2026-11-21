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
        public async Task ExecuteScalarRequestsAsync_ScalarCandidates_RespectBothBatchBoundaries(
            bool convertedKeys,
            int candidates,
            int requests
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateScalarContextAsync(
                Engine,
                convertedKeys,
                cancellationToken: TestContext.Current.CancellationToken,
                probe);

            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var otherTree = Guid.NewGuid();
            var requestedTrees = Enumerable
                .Range(0, requests)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            await SeedScalarRequestsAsync(
                context,
                convertedKeys,
                requestedTrees,
                root,
                cancellationToken: TestContext.Current.CancellationToken);

            await SeedScalarTreeAsync(
                context,
                convertedKeys,
                otherTree,
                root + 100,
                candidates,
                cancellationToken: TestContext.Current.CancellationToken);

            await TrackScalarChildrenAsync(
                context,
                convertedKeys,
                otherTree,
                cancellationToken: TestContext.Current.CancellationToken);

            var before = ScalarSnapshots(context, convertedKeys);
            await using var baseline = await CreateScalarContextAsync(
                Engine,
                convertedKeys,
                cancellationToken: TestContext.Current.CancellationToken);

            await ExecuteScalarRequestsAsync(
                baseline,
                convertedKeys,
                requestedTrees,
                static _ => Task.CompletedTask,
                cancellationToken: TestContext.Current.CancellationToken);

            var baselineBefore = GC.GetTotalAllocatedBytes(precise: true);
            await ExecuteScalarRequestsAsync(
                baseline,
                convertedKeys,
                requestedTrees,
                static _ => Task.CompletedTask,
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
                },
                cancellationToken: TestContext.Current.CancellationToken));

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
                Assert.Equal(
                    convertedKeys
                        ? 66
                        : 71,
                    probe.MaximumNativeParameters);
            }

            Assert.Equal(candidates, before.Length);
            Assert.Equal(before, ScalarSnapshots(context, convertedKeys));
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Equal(
                candidates + 1,
                await ScalarTreeCountAsync(
                    context,
                    convertedKeys,
                    otherTree,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(
                1,
                await ScalarTreeCountAsync(
                    context,
                    convertedKeys,
                    requestedTrees[^1],
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Null(context.Database.CurrentTransaction);
        }

        /// <summary>An affected persisted leaf in the final candidate and request batches stops payload work.</summary>
        /// <param name="convertedKeys">Whether the key follows the explicit-conversion scalar fallback.</param>
        /// <param name="candidates">The size of the real tracked candidate set.</param>
        /// <param name="requests">The number of active requested trees.</param>
        [Theory]
        [MemberData(nameof(ScalarCollectionCases))]
        public async Task ExecuteScalarRequestsAsync_ScalarLastCandidateInLastRequestBatch_RejectsStaleMembership(
            bool convertedKeys,
            int candidates,
            int requests
        )
        {
            // Arrange
            var probe = new NativeGuardProbe();
            await using var context = await CreateScalarContextAsync(
                Engine,
                convertedKeys,
                cancellationToken: TestContext.Current.CancellationToken,
                probe);

            var root = NativeTrackedIdentityGuardTestSupport.NextRoot(NativeCandidateCount + 1000);
            var otherTree = Guid.NewGuid();
            var requestedTrees = Enumerable
                .Range(0, requests)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            await SeedScalarRequestsAsync(
                context,
                convertedKeys,
                requestedTrees,
                root,
                cancellationToken: TestContext.Current.CancellationToken);

            await SeedScalarTreeAsync(
                context,
                convertedKeys,
                otherTree,
                root + 100,
                candidates,
                cancellationToken: TestContext.Current.CancellationToken);

            await TrackScalarChildrenAsync(
                context,
                convertedKeys,
                otherTree,
                cancellationToken: TestContext.Current.CancellationToken);

            var before = ScalarSnapshots(context, convertedKeys);
            await using var writer = await CreateScalarContextAsync(
                Engine,
                convertedKeys,
                cancellationToken: TestContext.Current.CancellationToken);

            // WHY: All candidates are leaves. Choosing the actual last tracker entry avoids depending on key or
            // attachment ordering; moving it through a separate context leaves this reader's TreeId genuinely stale.
            await MoveLastScalarCandidateAsync(
                context,
                writer,
                convertedKeys,
                root + requests - 1,
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
                },
                cancellationToken: TestContext.Current.CancellationToken));

            // Assert
            Assert.Equal(
                NestedSetErrorCode.InvalidContext,
                Assert.IsType<NestedSetException>(failure)
                    .Code);

            Assert.Equal(0, calls);
            Assert.Equal((candidates + 767) / 768 * ((requests + 63) / 64), probe.NativeReads);
            Assert.InRange(probe.MaximumNativeParameters, 1, 1664);
            Assert.Equal(candidates, before.Length);
            Assert.Equal(before, ScalarSnapshots(context, convertedKeys));
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Equal(
                candidates,
                await ScalarTreeCountAsync(
                    context,
                    convertedKeys,
                    otherTree,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(
                2,
                await ScalarTreeCountAsync(
                    context,
                    convertedKeys,
                    requestedTrees[^1],
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Null(context.Database.CurrentTransaction);
        }
    }
}
