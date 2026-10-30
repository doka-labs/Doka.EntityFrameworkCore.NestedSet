using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies throwing CLR accessors do not leave detached import inputs retained by the context.</summary>
[Collection("Allocation measurements")]
public sealed partial class BulkStagingRetentionTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    private static int s_rejectShadowComparison;

    /// <summary>Uses the fixture's exact SQLite engine selection.</summary>
    /// <param name="fixture">The provider fixture bound to this suite.</param>
    public BulkStagingRetentionTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>A shadow assignment rejected before tracking still leaves its root under import ownership.</summary>
    [Fact]
    public async Task ShadowAssignmentFailureReleasesTheUntrackedRoot()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<ShadowStagingContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider.UseNestedSets())
            .Options;

        await using var context = new ShadowStagingContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new StagingMarker { Id = 7 };
        context.Attach(marker);
        var trackingEvents = 0;
        context.ChangeTracker.Tracking += (_, _) => trackingEvents++;
        Interlocked.Exchange(ref s_rejectShadowComparison, 1);

        // Act
        var result = await ImportShadowWithoutRetainingInputAsync(context);

        // Assert
        Assert.IsType<InjectedCommandException>(result.Error);
        Assert.True(result.Restored);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.Equal(0, trackingEvents);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Generated getters and structural setters fail before the input enters the state manager.</summary>
    /// <param name="getter">Whether the one-shot failure comes from a generated CLR getter.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStagingReleasesItsInputAndPreservesUnrelatedTracking(
        bool getter
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<StagingContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider.UseNestedSets())
            .Options;

        await using var context = new StagingContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new StagingMarker { Id = 7 };
        context.Attach(marker);

        // Act
        var result = await ImportWithoutRetainingInputAsync(context, getter);

        // Assert
        Assert.True(result.Error?.GetBaseException() is InjectedCommandException, result.Error?.ToString());
        Assert.True(result.Restored);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(0, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Identity conflicts release pre-state references and preserve the legitimate tracked entity.</summary>
    /// <param name="alternate">Whether the conflict follows a successfully installed primary identity.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdentityConflictReleasesInputWithoutRemovingExistingIdentity(
        bool alternate
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<StagingContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider.UseNestedSets())
            .Options;

        await using var context = new StagingContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var existing = new StagingNode
        {
            Id = 1,
            Alias = "existing",
        };

        await context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertRootAsync(existing, Guid.NewGuid(), CancellationToken.None);

        context.Attach(existing);
        var marker = new StagingMarker { Id = 7 };
        context.Attach(marker);

        // Act
        var result = await ConflictingImportWithoutRetainingInputAsync(context, alternate);

        // Assert
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.True(result.Restored);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.Equal(2, context.ChangeTracker.Entries().Count());
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(existing).State);
        Assert.Same(existing, await context.FindAsync<StagingNode>([1], CancellationToken.None));
        Assert.Null(await context.FindAsync<StagingNode>([2], CancellationToken.None));
        Assert.Equal(1, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }
}
