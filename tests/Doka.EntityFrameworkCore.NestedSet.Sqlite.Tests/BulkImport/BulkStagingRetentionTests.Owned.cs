using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkStagingRetentionTests
{
    /// <summary>A rejected owned child leaves neither its detached handle nor its previously tracked parent.</summary>
    [Fact]
    public async Task OwnedTrackingFailureReleasesTheCompletePartialGraph()
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
        var parentTrackingEvents = 0;
        var childTrackingEvents = 0;
        var childStateEvents = 0;
        context.ChangeTracker.Tracking += (_, arguments) =>
        {
            if (arguments.Entry.Entity is StagingNode)
            {
                parentTrackingEvents++;
            }

            if (arguments.Entry.Entity is StagingPayload)
            {
                childTrackingEvents++;

                throw new InjectedCommandException();
            }
        };

        context.ChangeTracker.StateChanged += (_, arguments) =>
        {
            if (arguments.Entry.Entity is StagingPayload)
            {
                childStateEvents++;
            }
        };

        // Act
        var result = await ImportWithoutRetainingInputAsync(context, getter: false, tracking: true, payload: true);

        // Assert
        Assert.True(result.Error?.GetBaseException() is InjectedCommandException, result.Error?.ToString());
        Assert.True(result.Restored);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.NotNull(result.Payload);
        Assert.False(result.Payload.TryGetTarget(out _));
        Assert.Equal(1, parentTrackingEvents);
        Assert.Equal(1, childTrackingEvents);
        Assert.Equal(0, childStateEvents);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(0, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Tracking callbacks preserve collectible inputs without introducing extra lifecycle events.</summary>
    /// <param name="fail">Whether the callback rejects the initial transition to Added.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackingCallbacksReleaseOwnedInputsAndPreserveUnrelatedTracking(
        bool fail
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
        var trackingEvents = 0;
        var changingEvents = 0;
        var changedEvents = 0;
        context.ChangeTracker.Tracking += (_, arguments) =>
        {
            if (arguments.Entry.Entity is StagingNode
                && arguments.State == EntityState.Added)
            {
                trackingEvents++;

                if (fail)
                {
                    throw new InjectedCommandException();
                }
            }
        };

        context.ChangeTracker.StateChanging += (_, _) => changingEvents++;
        context.ChangeTracker.StateChanged += (_, _) => changedEvents++;

        // Act
        var result = await ImportWithoutRetainingInputAsync(context, getter: false, tracking: true);

        // Assert
        if (fail)
        {
            Assert.True(result.Error?.GetBaseException() is InjectedCommandException, result.Error?.ToString());
            Assert.True(result.Restored);
            Assert.Equal(0, changingEvents);
            Assert.Equal(0, changedEvents);
        }
        else
        {
            Assert.Null(result.Error);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.Equal(1, trackingEvents);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(fail ? 0 : 1, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>The same caller input remains persistable after a rejected first tracking.</summary>
    [Fact]
    public async Task TrackingFailureAllowsInputRetry()
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
        var trees = new[]
        {
            new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input)),
        };

        var trackingEvents = 0;
        context.ChangeTracker.Tracking += (_, arguments) =>
        {
            if (arguments.Entry.Entity is StagingNode)
            {
                trackingEvents++;

                if (trackingEvents == 1)
                {
                    throw new InjectedCommandException();
                }
            }
        };

        var rejection = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(trees, CancellationToken.None));

        // Act
        await context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(trees, CancellationToken.None);

        // Assert
        Assert.IsType<InjectedCommandException>(rejection);
        Assert.Equal(2, trackingEvents);
        Assert.Equal(1, input.Left);
        Assert.Equal(2, input.Right);
        Assert.Equal(1, await context.Set<StagingNode>().CountAsync(CancellationToken.None));

        var storedMarker = await context
            .Set<StagingMarker>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Equal("before", storedMarker.Name);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
    }

    /// <summary>A root callback can observe owned metadata without retaining either input after it throws.</summary>
    [Fact]
    public async Task RootTrackingFailureReleasesObservedOwnedInput()
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
        var result = await ImportWithoutRetainingInputAsync(context, getter: false, tracking: true, payload: true);

        // Assert
        Assert.IsType<InjectedCommandException>(result.Error);
        Assert.Equal(EntityState.Detached, observed);
        Assert.True(result.Restored);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.False(result.Payload!.TryGetTarget(out _));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Import rejects a tracked owned payload while preserving its caller-owned aggregate.</summary>
    [Fact]
    public async Task TrackedOwnedPayloadRejectionPreservesItsExistingAggregate()
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

        var payload = new StagingPayload { Label = "existing" };
        var existing = new StagingNode
        {
            Id = 2,
            Alias = "existing",
            Payload = payload,
        };

        await context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertRootAsync(existing, Guid.NewGuid(), CancellationToken.None);

        context.Attach(existing);
        var ownedKey = context
            .Entry(payload)
            .Metadata
            .FindOwnership()!.Properties.Single();

        var input = new StagingNode
        {
            Id = 1,
            Payload = payload,
        };

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
        Assert.Same(payload, existing.Payload);
        Assert.Same(payload, input.Payload);
        Assert.Equal(EntityState.Unchanged, context.Entry(existing).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(payload).State);
        Assert.Equal(2, context.Entry(payload).Property(ownedKey.Name).CurrentValue);
        Assert.Equal(2, context.ChangeTracker.Entries().Count());
        Assert.Equal(1, await context.Set<StagingNode>().CountAsync(CancellationToken.None));
    }
}
