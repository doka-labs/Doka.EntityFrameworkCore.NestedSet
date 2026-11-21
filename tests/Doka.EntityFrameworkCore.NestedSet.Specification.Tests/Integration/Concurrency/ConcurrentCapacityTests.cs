using static Doka.EntityFrameworkCore.NestedSet.Tests.ConcurrentCapacityTestSupport;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies tree-lock independence and contention with 64 real relational writers.</summary>
public abstract class ConcurrentCapacityTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the provider suite's isolated database fixture.</summary>
    /// <param name="fixture">The fixture owning the selected relational engine.</param>
    protected ConcurrentCapacityTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Holds 64 different existing tree locks before any writer can update hierarchy structure.</summary>
    /// <returns>A task that completes after all commits and full validation of every tree.</returns>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite's database-wide writer lock prevents simultaneous independent write transactions.")]
    public async Task InsertIndependentChildAsync_IndependentExistingTrees_Hold64LocksInOneScope()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var operationCancellationToken = TestContext.Current.CancellationToken;
        await using (var setup = database.CreateContext())
        {
            var hierarchy = setup
                .NestedSet<ConcurrentNode>()
                .ForScope(Scope);

            for (var index = 1; index <= WriterCount; index++)
            {
                await hierarchy.InsertRootAsync(
                    new ConcurrentNode { Id = index },
                    TreeId(index),
                    operationCancellationToken);
            }
        }

        var barrier = new ConcurrentCapacityBarrier(WriterCount);
        var writerIndexes = Enumerable
            .Range(1, WriterCount)
            .ToArray();

        // Act
        var acquiredLocks = await Task.WhenAll(
            writerIndexes.Select(
                index => InsertIndependentChildAsync(database, index, barrier, operationCancellationToken)));

        // Assert
        Assert.Equal(WriterCount, barrier.Arrivals);
        Assert.Equal(WriterCount, acquiredLocks.Length);
        Assert.All(acquiredLocks, count => Assert.Equal(1, count));
        await using var verification = database.CreateContext();
        var nodes = await verification
            .Set<ConcurrentNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(operationCancellationToken);

        Assert.Equal(WriterCount * 2, nodes.Length);
        Assert.All(nodes, node => Assert.Equal(Scope, node.Tree));
        Assert.Equal(
            WriterCount,
            nodes
                .Select(node => node.TreeId)
                .Distinct()
                .Count());

        for (var index = 1; index <= WriterCount; index++)
        {
            var root = nodes[index - 1];
            var child = nodes[WriterCount + index - 1];
            Assert.Equal(TreeId(index), root.TreeId);
            Assert.Equal(root.TreeId, child.TreeId);
            Assert.Equal(
                (1L, 4L, 0, 0L, (int?)null),
                (root.Left, root.Right, root.Depth, root.Position, root.ParentId));

            Assert.Equal(
                (2L, 3L, 1, 0L, (int?)root.Id),
                (child.Left, child.Right, child.Depth, child.Position, child.ParentId));

            Assert.Empty(
                (await verification
                    .NestedSet<ConcurrentNode>()
                    .ForScope(Scope)
                    .InTree(root.TreeId)
                    .ValidateAsync(NestedSetValidationLevel.Full, operationCancellationToken)).Issues);
        }
    }

    /// <summary>Cancels a blocked tree writer while preserving both callers' earlier application writes.</summary>
    /// <returns>A task that completes after both callers commit and the tree passes full validation.</returns>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite's database-wide writer lock prevents a second caller from saving prior application writes.")]
    public async Task InsertChildAsync_CanceledBlockedContender_PreservesBothCallerTransactions()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var treeId = TreeId(1);
        var operationCancellationToken = TestContext.Current.CancellationToken;
        await using (var setup = database.CreateContext())
        {
            await setup
                .NestedSet<ConcurrentNode>()
                .ForScope(Scope)
                .InsertRootAsync(new ConcurrentNode { Id = 1 }, treeId, operationCancellationToken);

            setup.AddRange(
                new UnrelatedRow
                {
                    Id = 1,
                    Value = "Initial owner value",
                },
                new UnrelatedRow
                {
                    Id = 2,
                    Value = "Initial contender value",
                });

            await setup.SaveChangesAsync(operationCancellationToken);
        }

        await using var owner = database.CreateContext();
        var isolation = NestedSetProviderCapabilities.Resolve(owner)
            .RequiredIsolation;

        await using var ownerTransaction = await owner.Database.BeginTransactionAsync(
            isolation,
            operationCancellationToken);
        var hierarchy = owner.Model.FindEntityType(typeof(ConcurrentNode))!;

        // WHY: Lock the registry before changing coordinates so the contender's parent-identity lookup
        // reaches its registry query rather than waiting on an uncommitted hierarchy row first.
        await NestedSetTreeLocks.AcquireAsync(
            owner,
            [new NestedSetTreeLockRequest<Guid, int>(hierarchy, Scope, treeId, NestedSetTreeLockMode.Existing)],
            operationCancellationToken);

        var ownerRow = await owner
            .Set<UnrelatedRow>()
            .SingleAsync(row => row.Id == 1, operationCancellationToken);

        ownerRow.Value = "Preserved owner write";
        await owner.SaveChangesAsync(operationCancellationToken);
        var probe = new ConcurrentCapacityCancellationProbe();
        await using var contender = database.CreateContext(probe);
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync(
            isolation,
            operationCancellationToken);

        var contenderRow = await contender
            .Set<UnrelatedRow>()
            .SingleAsync(row => row.Id == 2, operationCancellationToken);

        contenderRow.Value = "Preserved contender write";
        await contender.SaveChangesAsync(operationCancellationToken);
        probe.Armed = true;
        var canceledChild = new ConcurrentNode { Id = 3 };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operationCancellationToken);

        // Act
        var blocked = Record.ExceptionAsync(() => contender
            .NestedSet<ConcurrentNode>()
            .ForScope(Scope)
            .InsertChildAsync(canceledChild, 1, cancellation.Token));

        await probe.Attempted.Task.WaitAsync(operationCancellationToken);

        // WHY: Let the provider dispatch its lock query before canceling; the other caller retains that exact
        // registry row throughout this bounded wait, and no interceptor simulates or delays the lock itself.
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        var failure = await blocked;
        var ownerStillOwnsTransaction = ReferenceEquals(ownerTransaction, owner.Database.CurrentTransaction);
        var contenderStillOwnsTransaction = ReferenceEquals(
            contenderTransaction,
            contender.Database.CurrentTransaction);

        await owner
            .NestedSet<ConcurrentNode>()
            .ForScope(Scope)
            .InsertChildAsync(new ConcurrentNode { Id = 2 }, 1, operationCancellationToken);

        await ownerTransaction.CommitAsync(operationCancellationToken);
        await contenderTransaction.CommitAsync(operationCancellationToken);

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.True(ownerStillOwnsTransaction);
        Assert.True(contenderStillOwnsTransaction);
        Assert.Equal(1, probe.LockAttempts);
        Assert.Equal(0, probe.AcquiredLocks);
        Assert.Equal(0, probe.StructuralWrites);
        Assert.False(contender.ChangeTracker.HasChanges());
        Assert.Equal(
            EntityState.Unchanged,
            contender.Entry(contenderRow)
                .State);

        Assert.Equal(
            EntityState.Detached,
            contender.Entry(canceledChild)
                .State);

        Assert.Equal(
            (0L, 0L, 0, 0L, (int?)null),
            (canceledChild.Left, canceledChild.Right, canceledChild.Depth, canceledChild.Position,
                canceledChild.ParentId));

        await using var verification = database.CreateContext();
        Assert.Collection(
            await verification
                .Set<UnrelatedRow>()
                .OrderBy(row => row.Id)
                .Select(row => row.Value)
                .ToArrayAsync(operationCancellationToken),
            value => Assert.Equal("Preserved owner write", value),
            value => Assert.Equal("Preserved contender write", value));

        Assert.Collection(
            await verification
                .Set<ConcurrentNode>()
                .OrderBy(node => node.Id)
                .Select(node => node.Id)
                .ToArrayAsync(operationCancellationToken),
            id => Assert.Equal(1, id),
            id => Assert.Equal(2, id));

        Assert.Empty(
            (await verification
                .NestedSet<ConcurrentNode>()
                .ForScope(Scope)
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, operationCancellationToken)).Issues);
    }

    /// <summary>Appends one child after acquiring its exact existing registry row with 63 other writers.</summary>
    private static async Task<int> InsertIndependentChildAsync(
        TestDatabase database,
        int index,
        ConcurrentCapacityBarrier barrier,
        CancellationToken cancellationToken
    )
    {
        var probe = new ConcurrentCapacityLockProbe(Scope, TreeId(index), barrier);
        await using var context = database.CreateContext(probe);
        await context
            .NestedSet<ConcurrentNode>()
            .ForScope(Scope)
            .InsertChildAsync(new ConcurrentNode { Id = WriterCount + index }, index, cancellationToken);

        return probe.AcquiredLocks;
    }
}
