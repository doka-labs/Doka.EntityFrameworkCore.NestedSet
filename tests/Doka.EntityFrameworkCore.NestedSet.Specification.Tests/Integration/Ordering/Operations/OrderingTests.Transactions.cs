namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>
    ///     Verifies a repair failure after a completed payload save rolls back both kinds of database change.
    /// </summary>
    [Fact]
    public async Task LateOrderingFailureRollsBackPayloadAndPreservesPendingRename()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(setup));
        var before = await SnapshotAsync(setup);
        var probe = new OrderingSaveProbe { FailAfterPayload = true };
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.IsType<OrderingInjectedException>(exception);
        Assert.Equal(1, probe.CompletedPayloadUpdates);
        Assert.True(probe.StructuralUpdates > 0);
        Assert.Equal(EntityState.Modified, context.Entry(renamed).State);
        Assert.Equal("Bravo", context.Entry(renamed).Property(node => node.Name).OriginalValue);
        Assert.Equal("Zulu", renamed.Name);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        AssertSnapshot(before, await SnapshotAsync(verification));
    }

    /// <summary>Verifies cancellation after payload execution rolls back with a live recovery token.</summary>
    [Fact]
    public async Task CancellationAfterPayloadSaveRollsBackTheRename()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(setup));
        var before = await SnapshotAsync(setup);
        using var cancellation = new CancellationTokenSource();
        var probe = new OrderingSaveProbe { CancelAfterPayload = cancellation };
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(1, probe.CompletedPayloadUpdates);
        Assert.True(probe.StructuralUpdates > 0);
        Assert.Equal(EntityState.Modified, context.Entry(renamed).State);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        AssertSnapshot(before, await SnapshotAsync(verification));
    }

    /// <summary>
    ///     Verifies cancellation before save issues no database command and retains caller-owned changes.
    /// </summary>
    [Fact]
    public async Task AlreadyCanceledSaveDoesNotWriteOrAcceptChanges()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(setup));
        var before = await SnapshotAsync(setup);
        var probe = new OrderingSaveProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";
        probe.Commands.Clear();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Empty(probe.Commands);
        Assert.Equal("Zulu", renamed.Name);
        Assert.Equal("Bravo", context.Entry(renamed).Property(node => node.Name).OriginalValue);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        AssertSnapshot(before, await SnapshotAsync(verification));
    }

    /// <summary>
    ///     Verifies a failed reorder preserves earlier work in a caller transaction without committing it.
    /// </summary>
    [Fact]
    public async Task LateFailurePreservesEarlierCallerWorkAndTransactionOwnership()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(setup));
        var before = await SnapshotAsync(setup);
        var probe = new OrderingSaveProbe { FailAfterPayload = true };
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        await using var transaction = await BeginAsync(context);
        await context.AddAsync(new OrderingMarker { Id = 1, Value = "earlier work" }, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var retainedOwnership = ReferenceEquals(transaction, context.Database.CurrentTransaction);
        await transaction.CommitAsync(CancellationToken.None);

        // Assert
        Assert.IsType<OrderingInjectedException>(exception);
        Assert.Equal(1, probe.CompletedPayloadUpdates);
        Assert.True(retainedOwnership);
        Assert.Equal(EntityState.Modified, context.Entry(renamed).State);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        AssertSnapshot(before, await SnapshotAsync(verification));
        Assert.Equal("earlier work", await verification.Set<OrderingMarker>()
            .Select(marker => marker.Value)
            .SingleAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Verifies caller rollback reverses an otherwise successful payload save and subtree relocation.
    /// </summary>
    [Fact]
    public async Task CallerRollbackUndoesSuccessfulRenameAndUnrelatedInsert()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(context));
        var before = await SnapshotAsync(context);
        await using var transaction = await BeginAsync(context);
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";
        await context.AddAsync(new OrderingMarker { Id = 1, Value = "provisional" }, CancellationToken.None);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var retainedOwnership = ReferenceEquals(transaction, context.Database.CurrentTransaction);
        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        Assert.True(retainedOwnership);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        AssertSnapshot(before, await SnapshotAsync(verification));
        Assert.Empty(await verification.Set<OrderingMarker>().ToArrayAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Verifies domain failure in SaveChanges prevents both payload acceptance and hierarchy movement.
    /// </summary>
    [Fact]
    public async Task FailedApplicationInsertDoesNotCommitTheRename()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(setup));
        await setup.AddAsync(new OrderingMarker { Id = 1, Value = "existing" }, CancellationToken.None);
        await setup.SaveChangesAsync(CancellationToken.None);
        var before = await SnapshotAsync(setup);
        await using var context = await _fixture.CreateContextAsync(Engine);
        var renamed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";
        await context.AddAsync(new OrderingMarker { Id = 1, Value = "duplicate" }, CancellationToken.None);

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<DbUpdateException>(exception);
        Assert.Equal(EntityState.Modified, context.Entry(renamed).State);
        Assert.Equal("Bravo", context.Entry(renamed).Property(node => node.Name).OriginalValue);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        AssertSnapshot(before, await SnapshotAsync(verification));
        Assert.Equal("existing", await verification.Set<OrderingMarker>()
            .Select(marker => marker.Value)
            .SingleAsync(CancellationToken.None));
    }
}
