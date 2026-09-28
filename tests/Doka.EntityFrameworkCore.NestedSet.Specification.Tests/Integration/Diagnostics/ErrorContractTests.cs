namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies machine-readable error distinctions against persisted hierarchy operations.</summary>
public abstract class ErrorContractTests : ProviderTest
{
    private readonly RelationalFixture _relational;
    private readonly OrderingFixture _ordering;

    /// <summary>Creates cases using independently reset databases owned by the reusable fixtures.</summary>
    /// <param name="relational">The reusable unordered hierarchy databases.</param>
    /// <param name="ordering">The reusable databases with strict sibling ordering.</param>
    protected ErrorContractTests(
        IProviderFixture<RelationalFixture> relational,
        IProviderFixture<OrderingFixture> ordering
    ) : base(relational)
    {
        _relational = relational.Value;
        _ordering = ordering.Value;
    }

    /// <summary>A lock write affecting no rows has its own code and cannot mutate the hierarchy.</summary>
    [Fact]
    public async Task UnacquiredWriteLockReportsItsOwnCode()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        var probe = new LockCommandFailureProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.LockAcquisitionFailed, error.Code);
        Assert.True(probe.Attempts > 0);
        Assert.Empty(
            await service
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>A provider failure during lock acquisition keeps its exact type and original instance.</summary>
    [Fact]
    public async Task ProviderLockFailureRemainsUnwrapped()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        var failure = new LockProviderFailureException();
        var probe = new LockCommandFailureProbe(failure);
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Assert.ThrowsAsync<LockProviderFailureException>(() =>
            service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Same(failure, error);
        Assert.Equal(1, probe.Attempts);
        Assert.Empty(
            await service
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>A move beneath one's own descendant is distinguishable from a missing-node failure.</summary>
    [Fact]
    public async Task MovingUnderADescendantReportsACycleAndPreservesTheTree()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        await service.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);

        // Act
        var error =
            await Assert.ThrowsAsync<NestedSetException>(() => service.MoveToAsync(1, 2, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.CycleDetected, error.Code);
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Empty(
            (await service
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);

        var keys = await service
            .TreeContaining(1)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 2], keys);
    }

    /// <summary>A key belonging to another scope is not exposed through the typed missing-node error.</summary>
    [Fact]
    public async Task OtherScopeNodeReportsNotFoundWithoutLeakingScopeOrKey()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var source = context
            .NestedSet<TreeNode>()
            .ForScope(734826);

        var other = context
            .NestedSet<TreeNode>()
            .ForScope(912853);

        await source.InsertRootAsync(new TreeNode { NodeId = 673452 }, Guid.Empty, CancellationToken.None);

        // Act
        var error =
            await Assert.ThrowsAsync<NestedSetException>(() => other.DeleteAsync(673452, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.NodeNotFound, error.Code);
        Assert.DoesNotContain("734826", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("912853", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("673452", error.Message, StringComparison.Ordinal);
        Assert.Single(
            await source
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Corrupt persisted coordinates are rejected before they can change another row.</summary>
    [Fact]
    public async Task InvalidBoundsReportInvalidStructure()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        await context
            .Set<TreeNode>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.End, node => node.Start + 2),
                CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => service.DeleteAsync(1, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, error.Code);
        Assert.Single(
            await service
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Tracked structural entities cannot silently become stale during a bulk hierarchy update.</summary>
    [Fact]
    public async Task TrackedHierarchyReportsInvalidContext()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        _ = await context
            .Set<TreeNode>()
            .SingleAsync(CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => service.DeleteAsync(1, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, error.Code);
        Assert.Single(
            await service
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Ambient transactions are distinguished from invalid tracker or hierarchy state.</summary>
    [Fact]
    public async Task AmbientTransactionReportsInvalidTransaction()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        using var ambient = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidTransaction, error.Code);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Repeated import references are rejected as invalid input before structural writes.</summary>
    [Fact]
    public async Task RepeatedImportEntityReportsInvalidImport()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var branch = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 });

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => service.InsertForestAsync(
            [
                new NestedSetTreeImport<TreeNode, Guid>(Guid.NewGuid(), branch),
                new NestedSetTreeImport<TreeNode, Guid>(Guid.NewGuid(), branch),
            ],
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, error.Code);
        Assert.Empty(
            await service
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Strict sibling rules reject explicit placement with a dedicated machine-readable reason.</summary>
    [Fact]
    public async Task StrictPlacementReportsManualPlacementNotAllowed()
    {
        // Arrange
        await using var context = await _ordering.ResetAsync(Engine);
        var service = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        await service.InsertRootAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Root",
            },
            Guid.Empty,
            CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => service.InsertAsFirstChildAsync(
            new OrderingNode
            {
                Id = 2,
                Name = "Child",
            },
            1,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.ManualPlacementNotAllowed, error.Code);
        Assert.Single(
            await service
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }
}
