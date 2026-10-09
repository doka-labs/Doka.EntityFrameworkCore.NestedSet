using System.Runtime.CompilerServices;
using Doka.NestedSet;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies throwing CLR accessors do not leave detached import inputs retained by the context.</summary>
[Collection("Allocation measurements")]
public sealed class BulkStagingRetentionTests : ProviderTest,
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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

    /// <summary>Contains failed shadow-assignment input in a completed frame before collection assertions.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<StagingNode> Input, Exception? Error, bool Restored)>
        ImportShadowWithoutRetainingInputAsync(
            ShadowStagingContext context
        )
    {
        var input = new StagingNode
        {
            Id = 1,
            Left = 17,
            Right = 18,
            Version = 91,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(123)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                CancellationToken.None));

        return (new WeakReference<StagingNode>(input), error, input is { Id: 1, Left: 17, Right: 18, Version: 91 });
    }

    /// <summary>Rejects one shadow assignment while leaving rollback and comparison retries available.</summary>
    private static bool SameShadowScope(
        int left,
        int right
    )
    {
        if (left == 0
            && right == 123
            && Interlocked.Exchange(ref s_rejectShadowComparison, 0) != 0)
        {
            throw new InjectedCommandException();
        }

        return left == right;
    }

    /// <summary>Maps a shadow scope to exercise ownership before the first graph callback.</summary>
    private sealed class ShadowStagingContext(DbContextOptions<ShadowStagingContext> options)
        : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            var node = builder.Entity<StagingNode>();
            node.ToTable("ShadowStagingRetentionNodes");
            node.Ignore(value => value.Scope);
            node.Ignore(value => value.Payload);
            node.Ignore(value => value.FailGetter);
            node.Ignore(value => value.FailSetter);
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node
                .Property(value => value.Version)
                .HasDefaultValue(0)
                .ValueGeneratedOnAddOrUpdate();
            node
                .Property<int>("ShadowScope")
                .Metadata
                .SetValueComparer(
                    new ValueComparer<int>(
                        (left, right) => SameShadowScope(left, right),
                        value => value,
                        value => value));

            node.HasKey("ShadowScope", nameof(StagingNode.Id));
            node.HasNestedSet(set => set
                .HasNodeKey(value => value.Id)
                .HasTreeId(value => value.TreeId)
                .HasScope("ShadowScope")
                .HasParent(value => value.ParentId));
            builder
                .Entity<StagingMarker>()
                .ToTable("ShadowStagingRetentionMarkers");
        }
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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

    /// <summary>A rejected owned child leaves neither its detached handle nor its previously tracked parent.</summary>
    [Fact]
    public async Task OwnedTrackingFailureReleasesTheCompletePartialGraph()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<StagingContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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

    /// <summary>Contains rejected aggregate and foreign references outside the collection assertion frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<StagingNode> Input, WeakReference<StagingPayload> Payload,
        WeakReference<StagingForeign> Foreign, Exception? Error)> ImportForeignWithoutRetainingInputAsync(
        StagingContext context,
        StagingForeign? existing
    )
    {
        var foreign = existing ?? new StagingForeign { Id = 7 };
        var payload = new StagingPayload { Foreign = foreign };
        var input = new StagingNode
        {
            Id = 1,
            Payload = payload,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                CancellationToken.None));

        return (new WeakReference<StagingNode>(input), new WeakReference<StagingPayload>(payload),
            new WeakReference<StagingForeign>(foreign), error);
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
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

    /// <summary>Keeps the strong input reference outside the caller's completed async frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<StagingNode> Input, Exception? Error, bool Restored)>
        ConflictingImportWithoutRetainingInputAsync(
            StagingContext context,
            bool alternate
        )
    {
        var input = new StagingNode
        {
            Id = alternate ? 2 : 1,
            Alias = alternate ? "existing" : "input",
            Scope = 9,
            ParentId = 999,
            Left = 17,
            Right = 18,
            Depth = 9,
            Position = 11,
            Version = 91,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                CancellationToken.None));

        var restored = input.Id == (alternate ? 2 : 1)
            && input is { Scope: 9, ParentId: 999, Left: 17, Right: 18, Depth: 9, Position: 11, Version: 91 };

        return (new WeakReference<StagingNode>(input), error, restored);
    }

    /// <summary>Keeps root and owned payload references outside the caller's completed async frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<StagingNode> Input, WeakReference<StagingPayload>? Payload,
        Exception? Error, bool Restored)> ImportWithoutRetainingInputAsync(
        StagingContext context,
        bool getter,
        bool tracking = false,
        bool payload = false
    )
    {
        var input = new StagingNode
        {
            Id = 1,
            Scope = 9,
            ParentId = 999,
            Left = 17,
            Right = 18,
            Depth = 9,
            Position = 11,
            Version = 91,
            FailGetter = getter,
            FailSetter = !getter && !tracking,
            Payload = payload ? new StagingPayload { Label = "original" } : null,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<StagingNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<StagingNode, Guid>(Guid.Empty, new NestedSetBranch<StagingNode>(input))],
                CancellationToken.None));

        var restored = input is { Scope: 9, ParentId: 999, Left: 17, Right: 18, Depth: 9, Position: 11, Version: 91 };

        return (new WeakReference<StagingNode>(input),
            input.Payload is { } detail ? new WeakReference<StagingPayload>(detail) : null, error, restored);
    }

    /// <summary>Maps accessor modes explicitly so the regression exercises application CLR callbacks.</summary>
    private sealed class StagingContext : NestedSetDbContext
    {
        /// <summary>Uses the isolated SQLite database configuration.</summary>
        internal StagingContext(
            DbContextOptions<StagingContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<StagingNode>();
            node.ToTable("StagingRetentionNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node.HasAlternateKey(value => value.Alias);
            node
                .Property(value => value.Left)
                .UsePropertyAccessMode(PropertyAccessMode.Property);
            node
                .Property(value => value.Version)
                .HasDefaultValue(0)
                .ValueGeneratedOnAddOrUpdate()
                .UsePropertyAccessMode(PropertyAccessMode.Property);

            node.Ignore(value => value.FailGetter);
            node.Ignore(value => value.FailSetter);
            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId));
            node.OwnsOne(
                value => value.Payload,
                payload => payload
                    .HasOne(value => value.Foreign)
                    .WithMany());

            modelBuilder
                .Entity<StagingMarker>()
                .ToTable("StagingRetentionMarkers");
            modelBuilder
                .Entity<StagingForeign>()
                .ToTable("StagingRetentionForeigns");
        }
    }

    /// <summary>Provides one-shot failures that permit exact rollback after their first invocation.</summary>
    private sealed class StagingNode : IScopedNestedSetNode<int, Guid, int>
    {
        private long _left;
        private int _version;

        /// <inheritdoc />
        public int Id { get; set; }

        /// <summary>Gets or sets the secondary identity used to exercise partially installed key maps.</summary>
        public string Alias { get; set; } = "input";

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the configured scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the nullable parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets an optional owned payload whose lifecycle belongs to the input aggregate.</summary>
        public StagingPayload? Payload { get; set; }

        /// <summary>Gets or sets whether the next generated getter throws.</summary>
        public bool FailGetter { get; set; }

        /// <summary>Gets or sets whether the next left-bound assignment throws.</summary>
        public bool FailSetter { get; set; }

        /// <inheritdoc />
        public long Left
        {
            get => _left;
            set
            {
                if (FailSetter)
                {
                    FailSetter = false;

                    throw new InjectedCommandException();
                }

                _left = value;
            }
        }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }

        /// <summary>Gets or sets the generated value whose initial capture can fail.</summary>
        public int Version
        {
            get
            {
                if (FailGetter)
                {
                    FailGetter = false;

                    throw new InjectedCommandException();
                }

                return _version;
            }
            set => _version = value;
        }
    }

    /// <summary>Provides a separately tracked owned child in the partial insertion graph.</summary>
    private sealed class StagingPayload
    {
        /// <summary>Gets or sets ordinary caller-owned payload.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Gets or sets a non-owned navigation whose populated graph must be rejected.</summary>
        public StagingForeign? Foreign { get; set; }
    }

    /// <summary>Provides foreign application data outside the explicit hierarchy aggregate.</summary>
    private sealed class StagingForeign
    {
        /// <summary>Gets or sets its conventional application identity.</summary>
        public int Id { get; set; }
    }

    /// <summary>Keeps unrelated context-owned tracking alive while the failed input is collected.</summary>
    private sealed class StagingMarker
    {
        /// <summary>Gets or sets the application-owned identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets application payload persisted after an import failure.</summary>
        public string Name { get; set; } = string.Empty;
    }
}
