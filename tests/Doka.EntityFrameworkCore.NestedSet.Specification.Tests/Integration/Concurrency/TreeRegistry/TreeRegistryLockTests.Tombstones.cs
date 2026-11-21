namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class TreeRegistryLockTests
{
    /// <summary>A tombstone rejects operations that require an existing active tree.</summary>
    [Fact]
    public async Task AcquireAsync_TombstonedTreeId_RejectsExistingLock()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var treeId = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireAsync(
                setup,
                7,
                treeId,
                NestedSetTreeLockMode.New,
                TestContext.Current.CancellationToken);

            await TombstoneAsync(setup, 7, treeId, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var context = database.CreateContext();

        // Act
        var existing = await Record.ExceptionAsync(() => TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            NestedSetTreeLockMode.Existing,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.TreeIdUnavailable,
            Assert.IsType<NestedSetException>(existing)
                .Code);
    }

    /// <summary>A tombstone rejects attempts to reserve its TreeId as a new tree.</summary>
    [Fact]
    public async Task AcquireAsync_TombstonedTreeId_RejectsNewReservation()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var treeId = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireAsync(
                setup,
                7,
                treeId,
                NestedSetTreeLockMode.New,
                TestContext.Current.CancellationToken);

            await TombstoneAsync(setup, 7, treeId, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var context = database.CreateContext();

        // Act
        var created = await Record.ExceptionAsync(() => TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            NestedSetTreeLockMode.New,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.TreeIdUnavailable,
            Assert.IsType<NestedSetException>(created)
                .Code);
    }

    /// <summary>Fixture seeding rejects a deleted tree identity without persisting precomputed rows.</summary>
    [Fact]
    public async Task SavePrecomputedHierarchyAsync_FixtureSeed_RejectsTombstonedTreeId()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var treeId = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireAsync(
                setup,
                7,
                treeId,
                NestedSetTreeLockMode.New,
                TestContext.Current.CancellationToken);

            await TombstoneAsync(setup, 7, treeId, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var context = database.CreateContext();
        var revisionBefore = await TreeRegistryLockTestSupport.ReadRevisionAsync(
            context,
            7,
            treeId,
            cancellationToken: TestContext.Current.CancellationToken);

        await context.AddAsync(
            new TreeNode
            {
                NodeId = 1,
                Tree = 7,
                TreeId = treeId,
                Start = 1,
                End = 2,
            },
            TestContext.Current.CancellationToken);

        // Act
        var failure = await Record.ExceptionAsync(
            () => context.SavePrecomputedHierarchyAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.TreeIdUnavailable,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        await using var verification = database.CreateContext();
        Assert.Equal(
            revisionBefore,
            await TreeRegistryLockTestSupport.ReadRevisionAsync(
                verification,
                7,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(
            await verification
                .Set<TreeNode>()
                .ToArrayAsync(TestContext.Current.CancellationToken));
    }
}
