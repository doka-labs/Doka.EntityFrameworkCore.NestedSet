namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Checks clean-context guard allocation and automatic detection using an isolated SQLite boundary.</summary>
[Collection("Allocation measurements")]
public sealed class MutationContextGuardTests : ProviderTest,
    IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains allocation evidence in the actual regression result.</summary>
    /// <param name="fixture">The immutable SQLite owner; individual tests own their database resources.</param>
    /// <param name="output">The sink recording measured execution evidence.</param>
    public MutationContextGuardTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _output = output;
    }

    /// <summary>Scans a large clean application tracker without allocating a wrapper for every entry.</summary>
    [Fact]
    public async Task LargeCleanPrefixDoesNotAllocateEntryWrappers()
    {
        // Arrange
        var options = new DbContextOptionsBuilder()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        await using var context = new StrictOrderingContext(options);
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
        var executor = new Execution.NestedSetMutationExecutor<OrderingNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!);

        // WHY: Changed entries enumerate first in EF, so a rejecting dirty entry would skip the clean prefix.
        // A successful warmed boundary proves every clean entry was considered, including transaction overhead.
        await executor.ExecuteAsync(
            _ => Task.CompletedTask,
            [
                new Execution.NestedSetTreeLockRequest<Guid, int>(
                    context.Model.FindEntityType(typeof(OrderingNode))!,
                    1,
                    Guid.Empty,
                    Execution.NestedSetTreeLockMode.New),
            ],
            CancellationToken.None);

        var requests = new[]
        {
            new Execution.NestedSetTreeLockRequest<Guid, int>(
                context.Model.FindEntityType(typeof(OrderingNode))!,
                1,
                Guid.Empty,
                Execution.NestedSetTreeLockMode.Existing),
        };

        var before = GC.GetAllocatedBytesForCurrentThread();
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
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        _output.WriteLine($"Tracked entries=20000; clean boundary allocated bytes={allocated}; detections={detected}");
        Assert.True(executed);
        Assert.Equal(0, detected);
        Assert.InRange(allocated, 1, 100_000);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>
    ///     Detects an ordinary unrelated CLR edit before the structural operation can access the database.
    /// </summary>
    [Fact]
    public async Task GuardDetectsPendingUnrelatedClrChangeExactlyOnce()
    {
        // Arrange
        var options = new DbContextOptionsBuilder()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        await using var context = new StrictOrderingContext(options);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var entity = new OrderingMarker { Id = 1 };
        context.Attach(entity);
        entity.Value = "Changed";
        var detected = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detected++;
        var executor = new Execution.NestedSetMutationExecutor<OrderingNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!);

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ => Task.CompletedTask,
            [
                new Execution.NestedSetTreeLockRequest<Guid, int>(
                    context.Model.FindEntityType(typeof(OrderingNode))!,
                    1,
                    Guid.Empty,
                    Execution.NestedSetTreeLockMode.Existing)
            ],
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, detected);
        Assert.True(
            context
                .Entry(entity)
                .Property(node => node.Value)
                .IsModified);
        Assert.Null(context.Database.CurrentTransaction);
    }
}
