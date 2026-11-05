namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Manual managed inserts still reject callback navigation edits before insertion payload SQL.</summary>
    [Theory]
    [InlineData("single")]
    [InlineData("subtree")]
    [InlineData("forest")]
    public async Task ManualManagedCallbacksCannotChangeHierarchyNavigation(string shape)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using (var setup = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            await setup.NestedSet<NavigationNode>().ForScope(1)
                .InsertRootAsync(new NavigationNode { Id = 100, Name = "Destination" }, s_manualDestinationTree,
                    CancellationToken.None);
        }

        var callback = new SaveBoundaryCallback();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, probe);

        var root = await context.Set<NavigationNode>().Include(node => node.Children)
            .SingleAsync(node => node.Id == 1, CancellationToken.None);

        var child = Assert.Single(root.Children, node => node.Id == 2);
        var sibling = Assert.Single(root.Children, node => node.Id == 3);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        int? writesAtCallback = null;
        callback.OnSaving = _ =>
        {
            // WHY: The FK remains untouched by this CLR assignment, so scalar-only validation cannot detect it.
            writesAtCallback = probe.Writes;
            child.Parent = sibling;
        };

        var inputs = ManualInsertionInputs(shape).Select(input => new NavigationNode
        {
            Id = input.Id,
            Name = input.Name ?? throw new InvalidOperationException("The manual insertion fixture requires names."),
        }).ToArray();

        var tree = context.NestedSet<NavigationNode>().ForScope(1);
        Func<Task> insert = shape switch
        {
            "single" => () => tree.InsertChildAsync(inputs[0], 100, CancellationToken.None),
            "subtree" => () => tree.InsertSubtreeAsync(
                new NestedSetBranch<NavigationNode>(inputs[0], [new NestedSetBranch<NavigationNode>(inputs[1])]),
                100, CancellationToken.None),
            "forest" => () => tree.InsertForestAsync(
                [
                    new NestedSetTreeImport<NavigationNode, Guid>(s_manualFirstImportTree,
                        new NestedSetBranch<NavigationNode>(inputs[0],
                            [new NestedSetBranch<NavigationNode>(inputs[1])])),
                    new NestedSetTreeImport<NavigationNode, Guid>(s_manualSecondImportTree,
                        new NestedSetBranch<NavigationNode>(inputs[2],
                            [new NestedSetBranch<NavigationNode>(inputs[3])])),
                ], CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var failure = await Record.ExceptionAsync(insert);
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedChild = await verification.Set<NavigationNode>().Where(node => node.Id == 2)
            .Select(node => node.ParentId).SingleAsync(CancellationToken.None);

        var destination = await verification.Set<NavigationNode>().Where(node => node.Id == 100)
            .Select(node => new { node.Left, node.Right }).SingleAsync(CancellationToken.None);

        var count = await verification.Set<NavigationNode>().CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.NotNull(writesAtCallback);
        Assert.Equal(writesAtCallback, probe.Writes);
        Assert.Equal(1, storedChild);
        Assert.Equal((1L, 2L), (destination.Left, destination.Right));
        Assert.Equal(4, count);
        Assert.All(inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
