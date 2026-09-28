namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies read coordination cannot reserve or revive a tree through its registry requests.</summary>
public sealed class TypedTreeReadBoundaryTests
{
    /// <summary>Rejects lifecycle transitions before opening a connection or executing the read delegate.</summary>
    /// <param name="modeName">The unsupported registry lifecycle requirement.</param>
    /// <returns>A task that completes after verifying the read boundary rejects the request.</returns>
    [Theory]
    [InlineData("New")]
    [InlineData("Tombstoned")]
    public async Task ReadBoundaryRejectsLifecycleTransitions(
        string modeName
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        await using var context = new TreeContext(options);
        var entityType = context.Model.FindEntityType(typeof(TreeNode))!;
        var executor = new NestedSetMutationExecutor<TreeNode, int, Guid, int>(context, entityType);
        var mode = Enum.Parse<NestedSetTreeLockMode>(modeName);
        var request = new NestedSetTreeLockRequest<Guid, int>(entityType, 1, Guid.Empty, mode);
        var attempts = 0;

        // Act
        var error = await Record.ExceptionAsync(() => executor.ExecuteReadAsync(
            _ =>
            {
                attempts++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, attempts);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
