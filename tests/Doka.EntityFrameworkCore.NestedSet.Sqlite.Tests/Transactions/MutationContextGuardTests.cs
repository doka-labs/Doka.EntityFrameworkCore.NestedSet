using Doka.EntityFrameworkCore.NestedSet.Execution;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Checks clean-context guard admission and automatic detection using an isolated SQLite boundary.</summary>
[Collection("Scale scenarios")]
public sealed class MutationContextGuardTests : ProviderTest,
    IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses an isolated provider fixture for deterministic tracker and persistence checks.</summary>
    /// <param name="fixture">The immutable SQLite owner; individual tests own their database resources.</param>
    public MutationContextGuardTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture)
    {
    }

    /// <summary>Admits a large clean application tracker without enabling automatic detection.</summary>
    [Fact]
    public async Task ExecuteAsync_LargeCleanTracker_PreservesManualDetectionBoundary()
    {
        // Arrange
        var options = new DbContextOptionsBuilder()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", provider => provider
                .CommandTimeout(0)
                .UseNestedSets())
            .Options;

        await using var context = new StrictOrderingContext(options);
        FunctionalTestContextPolicy.Configure(context);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var entries = Enumerable
            .Range(1, 20_000)
            .Select(id => new OrderingMarker { Id = id })
            .ToArray();

        context.AttachRange(entries);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var detected = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detected++;
        var executor = new NestedSetMutationExecutor<OrderingNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!);

        // WHY: Changed entries enumerate first in EF, so a rejecting dirty entry would skip the clean prefix.
        // A successful warmed boundary proves every clean entry was considered, including transaction overhead.
        await executor.ExecuteAsync(
            _ => Task.CompletedTask,
            [
                new NestedSetTreeLockRequest<Guid, int>(
                    context.Model.FindEntityType(typeof(OrderingNode))!,
                    1,
                    Guid.Empty,
                    NestedSetTreeLockMode.New),
            ],
            CancellationToken.None);

        var requests = new[]
        {
            new NestedSetTreeLockRequest<Guid, int>(
                context.Model.FindEntityType(typeof(OrderingNode))!,
                1,
                Guid.Empty,
                NestedSetTreeLockMode.Existing),
        };

        var executed = false;

        // Act
        await executor.ExecuteAsync(
            _ =>
            {
                executed = true;

                return Task.CompletedTask;
            },
            requests,
            CancellationToken.None);

        // Assert
        Assert.True(executed);
        Assert.Equal(0, detected);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>
    ///     Detects an ordinary unrelated CLR edit before the structural operation can access the database.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_Guard_DetectsPendingUnrelatedClrChangeExactlyOnce()
    {
        // Arrange
        var options = new DbContextOptionsBuilder()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", provider => provider
                .CommandTimeout(0)
                .UseNestedSets())
            .Options;

        await using var context = new StrictOrderingContext(options);
        FunctionalTestContextPolicy.Configure(context);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var entity = new OrderingMarker { Id = 1 };
        context.Attach(entity);
        entity.Value = "Changed";
        var detected = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detected++;
        var executor = new NestedSetMutationExecutor<OrderingNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!);

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ => Task.CompletedTask,
            [
                new NestedSetTreeLockRequest<Guid, int>(
                    context.Model.FindEntityType(typeof(OrderingNode))!,
                    1,
                    Guid.Empty,
                    NestedSetTreeLockMode.Existing),
            ],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(1, detected);
        Assert.True(
            context
                .Entry(entity)
                .Property(node => node.Value)
                .IsModified);

        Assert.Null(context.Database.CurrentTransaction);
    }
}
