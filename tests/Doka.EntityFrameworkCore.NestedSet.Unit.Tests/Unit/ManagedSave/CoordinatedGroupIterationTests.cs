namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects every save stage when one context coordinates more than one hierarchy owner.</summary>
public sealed partial class CoordinatedGroupIterationTests
{
    /// <summary>Moves and refreshes both owners without treating the last hierarchy group as complete early.</summary>
    [Fact]
    public async Task ParentChangesApplyToEveryHierarchyOwner()
    {
        // Arrange
        await using var context = await CreateContextAsync();
        var first = await context.Set<GroupNode<FirstOwner>>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        var second = await context.Set<GroupNode<SecondOwner>>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        first.ParentId = 3;
        first.Name = "Moved first";
        second.ParentId = 3;
        second.Name = "Moved second";

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, saved);
        Assert.Equal(3, first.ParentId);
        Assert.Equal(3, second.ParentId);
        Assert.Equal(2, first.Depth);
        Assert.Equal(2, second.Depth);
        Assert.Equal(EntityState.Unchanged, context.Entry(first).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(second).State);
        await AssertMovedTreeAsync<FirstOwner>(context, "Moved first");
        await AssertMovedTreeAsync<SecondOwner>(context, "Moved second");
    }

    /// <summary>A payload failure restores both pending parent changes and rolls back both physical trees.</summary>
    [Fact]
    public async Task PayloadFailureRestoresEveryHierarchyOwner()
    {
        // Arrange
        await using var context = await CreateContextAsync();
        var first = await context.Set<GroupNode<FirstOwner>>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        var second = await context.Set<GroupNode<SecondOwner>>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        first.ParentId = 3;
        first.Name = "Moved first";
        second.ParentId = 3;
        second.Name = "Moved second";
        context.FailAfterPayload = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.IsType<PayloadFailureException>(failure);
        Assert.Equal(3, first.ParentId);
        Assert.Equal(3, second.ParentId);
        Assert.Equal("Moved first", first.Name);
        Assert.Equal("Moved second", second.Name);
        Assert.Equal(EntityState.Modified, context.Entry(first).State);
        Assert.Equal(EntityState.Modified, context.Entry(second).State);
        Assert.True(context.Entry(first).Property(node => node.ParentId).IsModified);
        Assert.True(context.Entry(second).Property(node => node.ParentId).IsModified);
        Assert.Equal(1, context.Entry(first).Property(node => node.ParentId).OriginalValue);
        Assert.Equal(1, context.Entry(second).Property(node => node.ParentId).OriginalValue);
        Assert.Null(context.Database.CurrentTransaction);
        await AssertOriginalTreeAsync<FirstOwner>(context);
        await AssertOriginalTreeAsync<SecondOwner>(context);
    }

    /// <summary>Verifies database geometry rather than returning the tracked objects that the save refreshed.</summary>
    private static async Task AssertMovedTreeAsync<TOwner>(
        GroupContext context,
        string expectedName
    )
    {
        var nodes = await context.Set<GroupNode<TOwner>>()
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 3, 2], nodes.Select(node => node.Id));
        Assert.Equal([0, 1, 2], nodes.Select(node => node.Depth));
        Assert.Equal([1L, 2L, 3L], nodes.Select(node => node.Left));
        Assert.Equal([6L, 5L, 4L], nodes.Select(node => node.Right));
        Assert.All(nodes, node => Assert.Equal(0, node.Position));
        Assert.Null(nodes[0].ParentId);
        Assert.Equal(1, nodes[1].ParentId);
        Assert.Equal(3, nodes[2].ParentId);
        Assert.Equal(expectedName, nodes[2].Name);
        Assert.Single(nodes.Select(node => node.TreeId).Distinct());
    }

    /// <summary>Reads the original sibling layout after the complete coordinated transaction rolls back.</summary>
    private static async Task AssertOriginalTreeAsync<TOwner>(
        GroupContext context
    )
    {
        var nodes = await context.Set<GroupNode<TOwner>>()
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 3, 2], nodes.Select(node => node.Id));
        Assert.Equal([0, 1, 1], nodes.Select(node => node.Depth));
        Assert.Equal([1L, 2L, 4L], nodes.Select(node => node.Left));
        Assert.Equal([6L, 3L, 5L], nodes.Select(node => node.Right));
        Assert.Equal([0L, 0L, 1L], nodes.Select(node => node.Position));
        Assert.Null(nodes[0].ParentId);
        Assert.Equal(1, nodes[1].ParentId);
        Assert.Equal(1, nodes[2].ParentId);
        Assert.Equal("Bravo", nodes[2].Name);
    }
}
