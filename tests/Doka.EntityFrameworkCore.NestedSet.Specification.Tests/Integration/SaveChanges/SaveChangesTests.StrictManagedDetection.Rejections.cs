namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Managed insertion still rejects raw callback changes to another tree's hierarchy roles.</summary>
    [Theory]
    [InlineData("single", "Name")]
    [InlineData("single", "ParentId")]
    [InlineData("single", "Left")]
    [InlineData("subtree", "Name")]
    [InlineData("subtree", "ParentId")]
    [InlineData("subtree", "Left")]
    [InlineData("forest", "Name")]
    [InlineData("forest", "ParentId")]
    [InlineData("forest", "Left")]
    public async Task InsertManualShapeAsync_ManualManagedCallbacks_CannotChangeHierarchyRoles(
        string shape,
        string role
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedManualDestinationAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        var probe = new SaveBoundaryProbe();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, callback, probe);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var before = await setup
            .Set<OrderingNode>()
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.Id,
                value.Name,
                value.ParentId,
                value.Left,
                value.Right,
            })
            .ToArrayAsync(CancellationToken.None);

        int? writesAtCallback = null;
        callback.OnSaving = _ =>
        {
            // WHY: Opening the target interval precedes the callback; no payload may execute after its illegal edit.
            writesAtCallback = probe.PayloadCommands;

            switch (role)
            {
                case "Name":
                    node.Name = "Zulu";
                    break;
                case "ParentId":
                    node.ParentId = 2;
                    break;
                case "Left":
                    node.Left++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        };

        var inputs = ManualInsertionInputs(shape);

        // Act
        var failure = await Record.ExceptionAsync(() => InsertManualShapeAsync(
            context,
            shape,
            inputs,
            cancellationToken: TestContext.Current.CancellationToken));

        var after = await setup
            .Set<OrderingNode>()
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.Id,
                value.Name,
                value.ParentId,
                value.Left,
                value.Right,
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.NotNull(writesAtCallback);
        Assert.Equal(writesAtCallback, probe.PayloadCommands);
        Assert.Equal(before, after);
        Assert.All(
            inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Manual mode does not bypass the existing facade rejection of initially registered writes.</summary>
    [Theory]
    [InlineData("single")]
    [InlineData("subtree")]
    [InlineData("forest")]
    public async Task InsertManualShapeAsync_ManualManagedInsertion_RejectsInitiallyRegisteredWrites(
        string shape
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedManualDestinationAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new SaveBoundaryProbe();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, probe);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var addition = new OrderingMarker
        {
            Id = 4,
            Value = "pending before facade",
        };

        await context.AddAsync(addition, CancellationToken.None);
        var inputs = ManualInsertionInputs(shape);

        // Act
        var error = await Record.ExceptionAsync(() => InsertManualShapeAsync(
            context,
            shape,
            inputs,
            cancellationToken: TestContext.Current.CancellationToken));

        var count = await setup
            .Set<OrderingNode>()
            .CountAsync(CancellationToken.None);

        var markers = await setup
            .Set<OrderingMarker>()
            .CountAsync(CancellationToken.None);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(error);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Contains("no pending tracked changes", rejection.Message, StringComparison.Ordinal);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(4, count);
        Assert.Equal(0, markers);
        Assert.Equal(
            EntityState.Added,
            context.Entry(addition)
                .State);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
