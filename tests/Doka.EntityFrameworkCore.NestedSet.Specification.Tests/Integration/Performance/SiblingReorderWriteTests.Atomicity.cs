namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SiblingReorderWriteTests
{
    /// <summary>Cancellation after one marking batch restores all bounds and preserves caller ownership.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterMarkingRestoresEveryCoordinate(
        bool callerTransaction
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var sizes = Enumerable
            .Repeat(1, 70)
            .ToArray();

        var names = Enumerable
            .Range(0, 70)
            .Select(index => $"Sibling-{69 - index:D3}")
            .ToArray();

        await SeedAsync(setup, sizes, names);
        using var cancellation = new CancellationTokenSource();
        var probe = new StructuralWriteProbe("OrderingNodes", "Left", "Right")
        {
            CancelAfterBounds = cancellation,
        };

        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var before = await SnapshotAsync(context);
        await using var transaction = callerTransaction
            ? await context.Database.BeginTransactionAsync(
                Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;

        // Act
        var error = await Record.ExceptionAsync(() => ReorderAsync(context, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(64, Assert.Single(probe.BoundsWrites).Rows);
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.Equal(callerTransaction, context.Database.CurrentTransaction is not null);
    }

    /// <summary>Reads all scopes to expose both incomplete staging rollback and accidental scope leakage.</summary>
    private static async Task<(int Id, long Left, long Right, int Depth, long Position, int? Parent)[]> SnapshotAsync(
        OrderingContext context
    )
    {
        var nodes = await context
            .Set<OrderingNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        return nodes
            .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position, node.ParentId))
            .ToArray();
    }
}
