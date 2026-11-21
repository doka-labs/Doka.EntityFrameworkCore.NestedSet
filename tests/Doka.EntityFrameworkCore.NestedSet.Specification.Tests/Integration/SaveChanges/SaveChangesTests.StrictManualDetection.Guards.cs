namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Raw parent and ordering changes require explicit tracking in manual mode, even on Modified nodes.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveChangesAsync_UnmarkedHierarchyChanges_AreRejectedInManualMode(
        bool parent,
        bool markedPayload
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new SaveBoundaryProbe();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, probe);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;

        if (parent)
        {
            node.ParentId = 2;
        }
        else
        {
            node.Name = "Zulu";
        }

        if (markedPayload)
        {
            context
                .Entry(node)
                .Property(value => value.Payload)
                .CurrentValue = "explicit payload";
        }

        await context.AddAsync(new OrderingMarker { Id = 1 }, CancellationToken.None);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var stored = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => new
            {
                value.ParentId,
                value.Name,
                value.Payload,
            })
            .SingleAsync(CancellationToken.None);

        var markers = await setup
            .Set<OrderingMarker>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(0, detections);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(0, markers);
        Assert.Equal(10, stored.ParentId);
        Assert.Equal("Alpha", stored.Name);
        Assert.Equal("original", stored.Payload);
        Assert.False(
            context
                .Entry(node)
                .Property(
                    parent
                        ? nameof(node.ParentId)
                        : nameof(node.Name))
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>
    ///     Rolls back explicit application writes while preserving the pre-save manual tracker write set.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_ManualHierarchyFailure_RestoresExplicitPendingWrites(
        bool parent
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await SeedManualMarkersAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var failure = new ManualSaveFailure();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, failure);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        var rows = await context
            .Set<OrderingMarker>()
            .OrderBy(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        rows[0].Value = "unmarked foreign";
        node.Payload = "unmarked same node";
        MarkManualHierarchyChange(context, node, parent, currentValue: true);
        context
            .Entry(rows[1])
            .Property(value => value.Value)
            .CurrentValue = "explicit update";

        context.Remove(rows[2]);
        var addition = new OrderingMarker
        {
            Id = 4,
            Value = "explicit addition",
        };

        await context.AddAsync(addition, CancellationToken.None);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedNode = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => new
            {
                value.ParentId,
                value.Name,
                value.Payload,
            })
            .SingleAsync(CancellationToken.None);

        var storedRows = await setup
            .Set<OrderingMarker>()
            .OrderBy(value => value.Id)
            .Select(value => value.Value)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, error);
        Assert.Equal(10, storedNode.ParentId);
        Assert.Equal("Alpha", storedNode.Name);
        Assert.Equal("original", storedNode.Payload);
        Assert.Equal(["original", "original", "original"], storedRows);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(rows[1])
                .State);

        Assert.Equal(
            EntityState.Deleted,
            context.Entry(rows[2])
                .State);

        Assert.Equal(
            EntityState.Added,
            context.Entry(addition)
                .State);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(rows[0])
                .State);

        Assert.False(
            context
                .Entry(node)
                .Property(value => value.Payload)
                .IsModified);

        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
