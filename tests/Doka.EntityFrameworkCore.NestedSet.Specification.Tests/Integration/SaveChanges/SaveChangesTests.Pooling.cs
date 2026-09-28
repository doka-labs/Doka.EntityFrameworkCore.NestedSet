namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Reuses the exact context instance after success, failure, or cancellation without stale save guards.
    /// </summary>
    [Theory]
    [InlineData("Success")]
    [InlineData("Failure")]
    [InlineData("Canceled")]
    public async Task PooledContextReusesCleanSaveIntegration(
        string outcome
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        var callback = new SaveBoundaryCallback();
        var factory = new PooledDbContextFactory<SaveBoundaryContext>(
            SaveBoundaryTestSupport.Options(setup, callback).Options,
            poolSize: 1);

        var first = await factory.CreateDbContextAsync(CancellationToken.None);
        var firstLease = first.ContextId.Lease;
        var node = await first
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        node.Name = "Zulu";
        using var cancellation = new CancellationTokenSource();
        callback.OnSaving = _ =>
        {
            if (outcome == "Failure")
            {
                throw new SaveBoundaryTransientException();
            }

            if (outcome == "Canceled")
            {
                // WHY: The synchronous callback must cancel before control returns to the save boundary.
                // ReSharper disable once MethodHasAsyncOverload
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        };

        // Act
        var firstFailure = await Record.ExceptionAsync(() => first.SaveChangesAsync(cancellation.Token));
        await first.DisposeAsync();
        callback.OnSaving = null;
        await using var second = await factory.CreateDbContextAsync(CancellationToken.None);
        var initiallyTracked = second
            .ChangeTracker
            .Entries()
            .Count();

        var secondNode = await second
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        secondNode.Name = "Aardvark";
        var saved = await second.SaveChangesAsync(CancellationToken.None);
        var tree = second
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var children = await tree
            .ChildrenOf(10)
            .Select(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        await using var verification = new SaveBoundaryContext(SaveBoundaryTestSupport.Options(setup).Options);
        var errors = await verification
            .NestedSet<OrderingNode>()
            .ForScope(1)
            .InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Same(first, second);
        Assert.True(second.ContextId.Lease > firstLease);
        Assert.Equal(0, initiallyTracked);
        Assert.Equal(1, saved);
        Assert.Equal([2, 1], children);
        Assert.Empty(errors.Issues);

        if (outcome == "Success")
        {
            Assert.Null(firstFailure);
        }
        else if (outcome == "Canceled")
        {
            Assert.IsAssignableFrom<OperationCanceledException>(firstFailure);
        }
        else
        {
            Assert.IsType<SaveBoundaryTransientException>(firstFailure);
        }
    }
}
