using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkStagingRetentionTests
{
    /// <summary>A root callback can observe owned metadata without retaining either input after it throws.</summary>
    [Fact]
    public async Task ImportWithoutRetainingInputAsync_RootTrackingFailure_ReleasesObservedOwnedInput()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<StagingContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider
                .CommandTimeout(0)
                .UseNestedSets())
            .Options;

        await using var context = new StagingContext(options);
        FunctionalTestContextPolicy.Configure(context);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new StagingMarker { Id = 7 };
        context.Attach(marker);
        EntityState? observed = null;
        context.ChangeTracker.Tracking += (_, arguments) =>
        {
            if (arguments.Entry.Entity is StagingNode { Payload: { } payload })
            {
                observed = context.Entry(payload)
                    .State;

                throw new InjectedCommandException();
            }
        };

        // Act
        var result = await ImportWithoutRetainingInputAsync(
            context,
            getter: false,
            tracking: true,
            payload: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<InjectedCommandException>(result.Error);
        Assert.Equal(EntityState.Detached, observed);
        Assert.True(result.Restored);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.False(result.Payload!.TryGetTarget(out _));
        Assert.Same(
            marker,
            Assert.Single(context.ChangeTracker.Entries())
                .Entity);

        Assert.Equal(
            0,
            await context
                .Set<StagingNode>()
                .CountAsync(CancellationToken.None));

        GC.KeepAlive(context);
    }
}
