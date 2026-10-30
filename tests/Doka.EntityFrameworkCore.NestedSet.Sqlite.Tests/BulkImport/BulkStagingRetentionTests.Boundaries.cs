using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkStagingRetentionTests
{
    /// <summary>Deep foreign navigation rejection releases new references and preserves caller-tracked data.</summary>
    /// <param name="tracked">Whether the foreign entity already belongs to the application tracker.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeepForeignNavigationRejectionPreservesOwnershipBoundaries(
        bool tracked
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

        StagingForeign? existing = null;

        if (tracked)
        {
            existing = new StagingForeign { Id = 7 };
            context.Add(existing);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        // Act
        var result = await ImportForeignWithoutRetainingInputAsync(context, existing);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(result.Error).Code);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.False(result.Payload.TryGetTarget(out _));
        Assert.Equal(tracked, result.Foreign.TryGetTarget(out _));
        Assert.Equal(tracked ? 1 : 0, context.ChangeTracker.Entries().Count());
        Assert.Equal(0, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
        Assert.Equal(tracked ? 1 : 0, await context.Set<StagingForeign>().CountAsync(CancellationToken.None));

        if (tracked)
        {
            Assert.Same(existing, await context.FindAsync<StagingForeign>([7], CancellationToken.None));
            Assert.Equal(EntityState.Unchanged, context.Entry(existing!).State);
        }

        GC.KeepAlive(context);
    }

    /// <summary>Ordinary application payload remains savable after a failed insertion tracking transition.</summary>
    [Fact]
    public async Task TrackingFailurePreservesUnrelatedApplicationSave()
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

        var marker = new StagingMarker
        {
            Id = 7,
            Name = "before",
        };

        context.Add(marker);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = new StagingNode { Id = 1 };
        context.ChangeTracker.Tracking += (_, arguments) =>
        {
            if (arguments.Entry.Entity is StagingNode)
            {
                throw new InjectedCommandException();
            }
        };

        var rejection = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                CancellationToken.None));

        marker.Name = "after";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.IsType<InjectedCommandException>(rejection);
        Assert.Equal(0, await context.Set<StagingNode>().CountAsync(CancellationToken.None));

        var storedMarker = await context
            .Set<StagingMarker>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Equal("after", storedMarker.Name);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
    }

    /// <summary>A rejected detachment preserves the tracking error and explicitly invalidates the context.</summary>
    [Fact]
    public async Task CleanupFailurePreservesTheTrackingErrorAndRequiresContextDiscard()
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
        context.ChangeTracker.Tracking += (_, arguments) =>
        {
            if (arguments.Entry.Entity is StagingPayload)
            {
                throw new InjectedCommandException();
            }
        };

        context.ChangeTracker.StateChanging += (_, arguments) =>
        {
            if (arguments.Entry.Entity is StagingNode
                && arguments.NewState == EntityState.Detached)
            {
                throw new InvalidOperationException("Rejected cleanup transition.");
            }
        };

        // Act
        var result = await ImportWithoutRetainingInputAsync(context, getter: false, tracking: true, payload: true);

        // Assert
        var error = Assert.IsType<AggregateException>(result.Error);
        var causes = error.Flatten().InnerExceptions;

        Assert.Contains(causes, cause => cause is InjectedCommandException);
        Assert.Contains(causes, cause => cause is InvalidOperationException && cause.Message == "Rejected cleanup transition.");

        Assert.Contains("Discard the context.", error.Message, StringComparison.Ordinal);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(0, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
    }
}
