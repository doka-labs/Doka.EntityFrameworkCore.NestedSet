namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks recovery of Deleted complex graphs, which EF cannot reinstall directly.</summary>
public sealed partial class ComplexDeletedElementRecoveryTests
{
    /// <summary>
    /// Reinstalls a pending Deleted element and persists its removal without EF overflowing the stack.
    /// </summary>
    /// <param name="middles">The number of second-level elements below the Deleted element.</param>
    /// <param name="tips">The number of third-level elements below each second-level element.</param>
    /// <param name="detectCallback">Whether EF observes the callback addition before recovery.</param>
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 2, false)]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, true)]
    public async Task PendingDeletedElementSurvivesCallbackAddition(
        int middles,
        int tips,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = await CreateAsync(
            NewPayload(1, NewOuter("Stored", middles, tips)),
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await context
            .Set<Payload>()
            .SingleAsync(CancellationToken.None);

        var entry = context.Entry(payload);
        var rows = payload.Rows;
        rows!.RemoveAt(0);
        context.ChangeTracker.DetectChanges();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var before = Lanes(entry);
        rows.Add(NewOuter("Callback", 1, 1));

        if (detectCallback)
        {
            // WHY: EF's detection can itself reject this graph. Recovery must succeed after a partial pass too.
            _ = Record.Exception(() => context.ChangeTracker.DetectChanges());
        }

        // Act
        snapshot.Restore();
        var restoredRows = payload.Rows;
        var restoredItems = rows.ToArray();
        var restoredLanes = Lanes(entry);
        var restoredState = Original(entry, 0)
            .State;

        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Same(rows, restoredRows);
        Assert.Empty(restoredItems);
        Assert.Equal(before, restoredLanes);
        Assert.Equal(EntityState.Deleted, restoredState);
        Assert.Equal("[]", await StoredRowsAsync(context, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Retires an Added child whose Deleted owner has no list position for EF's ordinal path.</summary>
    /// <param name="withChild">Whether the pending Added element owns its own nested element.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingNestedAddedSurvivesRemovalOfItsOwner(
        bool withChild
    )
    {
        // Arrange
        await using var context = await CreateAsync(
            NewPayload(2, NewOuter("Stored", 1, 1)),
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await context
            .Set<Payload>()
            .SingleAsync(CancellationToken.None);

        var entry = context.Entry(payload);
        var rows = payload.Rows!;
        var owner = rows[0];
        owner.Middles!.Insert(
            0,
            NewMiddle(
                "Pending",
                withChild
                    ? 1
                    : 0));

        context.ChangeTracker.DetectChanges();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var before = Lanes(entry);
        rows.Clear();
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();
        var restoredRows = rows.ToArray();
        var restoredLanes = Lanes(entry);
        var restoredState = Current(entry, 0, 0)
            .State;

        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Same(owner, Assert.Single(restoredRows));
        Assert.Equal(before, restoredLanes);
        Assert.Equal(EntityState.Added, restoredState);
        Assert.Contains(
            "Pending",
            await StoredRowsAsync(
                context,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Addresses each retiring entry through its own instance after a callback shifted its ordinal.</summary>
    [Fact]
    public async Task NestedInsertionBeforeRemovedOwnerRetiresThroughLiveOrdinals()
    {
        // Arrange
        await using var context = await CreateAsync(
            NewPayload(3, NewOuter("Stored", 1, 2)),
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await context
            .Set<Payload>()
            .SingleAsync(CancellationToken.None);

        var entry = context.Entry(payload);
        var rows = payload.Rows!;
        var owner = rows[0];
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var before = Lanes(entry);
        owner.Middles![0].Tips!.Add(new Tip { Name = "Callback tip" });
        owner.Middles.Insert(0, NewMiddle("Callback middle", 1));
        context.ChangeTracker.DetectChanges();
        rows.RemoveAt(0);
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(owner, Assert.Single(rows));
        Assert.Equal(1, owner.Middles.Count(middle => middle.Name == "Stored 0"));
        Assert.Equal(before, Lanes(entry));
        Assert.Equal(
            EntityState.Unchanged,
            Current(entry, 0, 0)
                .State);
    }

    /// <summary>Restores a callback's entity deletion without letting EF's transition keep raw lane growth.</summary>
    [Fact]
    public async Task CallbackEntityDeletionKeepsCapturedLaneLengths()
    {
        // Arrange
        await using var context = await CreateAsync(
            NewPayload(4, NewOuter("Stored", 1, 1)),
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await context
            .Set<Payload>()
            .SingleAsync(CancellationToken.None);

        var entry = context.Entry(payload);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        payload.Rows!.Add(NewOuter("Raw", 0, 0));
        payload.Marker = "Pending";
        entry.Property(value => value.Marker)
            .IsModified = true;

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var before = Lanes(entry);
        context.Remove(payload);

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(before, Lanes(entry));
        Assert.Equal(2, payload.Rows.Count);
    }

    /// <summary>Captures a Deleted entity whose elements hold value-type references without originals.</summary>
    /// <param name="callbackRestore">Whether the callback resurrects the entity before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedEntityWithStructElementsCanBeCapturedAndRestored(
        bool callbackRestore
    )
    {
        // Arrange
        await using var context = await CreateAsync(
            NewPayload(5, NewOuter("Stored", 1, 1)),
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await context
            .Set<Payload>()
            .SingleAsync(CancellationToken.None);

        var entry = context.Entry(payload);
        context.Remove(payload);

        // Act
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        if (callbackRestore)
        {
            entry.State = EntityState.Unchanged;
        }

        snapshot.Restore();
        var restoredState = entry.State;
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(EntityState.Deleted, restoredState);
        Assert.Equal(
            0,
            await context
                .Set<Payload>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Restores a pending Added element below a Deleted entity, which EF exposes through no entry.</summary>
    [Fact]
    public async Task PendingAddedElementBelowDeletedEntityRestoresItsScalars()
    {
        // Arrange
        await using var context = await CreateAsync(
            NewPayload(7, NewOuter("Stored", 0, 0)),
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await context
            .Set<Payload>()
            .SingleAsync(CancellationToken.None);

        var entry = context.Entry(payload);
        var pending = NewOuter("Pending", 0, 0);
        payload.Rows!.Add(pending);
        context.ChangeTracker.DetectChanges();
        context.Remove(payload);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var before = Lanes(entry);
        pending.Name = "Callback";

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal("Pending", pending.Name);
        Assert.Equal(EntityState.Deleted, entry.State);
        Assert.Equal(before, Lanes(entry));
    }

    /// <summary>Rebuilds Deleted elements that a resurrected entity retains in current slots.</summary>
    /// <param name="middles">The number of second-level elements holding Deleted children below live owners.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ResurrectedEntityRetainsItsDeletedElements(
        int middles
    )
    {
        // Arrange
        await using var context = await CreateAsync(
            NewPayload(8, NewOuter("Stored", middles, 2)),
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await context
            .Set<Payload>()
            .SingleAsync(CancellationToken.None);

        var entry = context.Entry(payload);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        context.Remove(payload);
        entry.State = EntityState.Modified;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var before = Lanes(entry);
        payload.Rows!.Add(NewOuter("Callback", 1, 1));
        context.ChangeTracker.DetectChanges();
        payload.Rows.RemoveAt(1);
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(before, Lanes(entry));
    }

    /// <summary>Validates a wrapper-created element that holds an original ordinal without an original slot.</summary>
    [Fact]
    public async Task ElementCreatedThroughWrapperWithoutOriginalLaneIsRestored()
    {
        // Arrange
        await using var context = await CreateAsync(
            new Payload
            {
                Id = 6,
                Marker = "Stored",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await context
            .Set<Payload>()
            .SingleAsync(CancellationToken.None);

        var entry = context.Entry(payload);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        payload.Rows = [NewOuter("Pending", 0, 0)];
        var element = entry
            .ComplexCollection(value => value.Rows)[0];

        element.Property(value => value.Name)
            .CurrentValue = "Edited";

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var before = Lanes(entry);
        payload.Rows = [];
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(
            "Edited",
            Assert.Single(payload.Rows!)
                .Name);

        Assert.Equal(before, Lanes(entry));
    }

    /// <summary>Creates a persisted single-row store and a fresh context sharing that connection.</summary>
    /// <param name="seed">The independent payload persisted before the recovery scenario.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static Task<DeletedContext> CreateAsync(
        Payload seed,
        CancellationToken cancellationToken
    )
        => CreateAsync(seed, new SqliteConnection("Data Source=:memory:"), cancellationToken: cancellationToken);

    /// <summary>Owns the supplied connection until initialization transfers it to the returned context.</summary>
    /// <param name="seed">The single payload to persist before recovery begins.</param>
    /// <param name="connection">The connection whose ownership transfers to this factory on entry.</param>
    /// <returns>A fresh context that owns the initialized connection.</returns>
    /// <exception cref="AggregateException">
    /// Initialization and cleanup both fail; the initialization error is first.
    /// </exception>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task<DeletedContext> CreateAsync(
        Payload seed,
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using (var context = new DeletedContext(connection, ownsConnection: false))
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
                context.Add(seed);
                await context.SaveChangesAsync(cancellationToken);
            }

            return new DeletedContext(connection, ownsConnection: true);
        }
        catch (Exception initializationFailure)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(initializationFailure, cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>Reads stored JSON without tracking anything or reusing the context's change tracker.</summary>
    /// <param name="context">The context used by this fixture operation.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task<string> StoredRowsAsync(
        DeletedContext context,
        CancellationToken cancellationToken
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT \"Rows\" FROM \"Payload\"";

        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Observes both cached native lanes at every level, including holes, independent of recovery.</summary>
    private static string Lanes(
        EntityEntry<Payload> entry
    )
    {
        var rows = entry.Metadata.FindComplexProperty(nameof(Payload.Rows))!;

        return Lanes(NestedSetComplexEntryAccess.Owner(entry.Property(value => value.Id)), rows);
    }

    /// <summary>Renders one owner's current and original lanes recursively.</summary>
    private static string Lanes(
        object owner,
        IComplexProperty property
    )
    {
        var parts = new List<string>();

        foreach (var original in new[] { false, true })
        {
            var entries = NestedSetComplexEntryAccess.Entries(owner, property, original);
            parts.Add(
                (original
                    ? "o"
                    : "c")
                + entries.Count
                + "["
                + string.Join(
                    ",",
                    entries
                        .Cast<object?>()
                        .Select(native => native is null
                            ? "hole"
                            : Describe(native, property)))
                + "]");
        }

        return property.Name + ":" + string.Join("/", parts);
    }

    /// <summary>Renders one native element's identity-free lifecycle, positions and nested lanes.</summary>
    private static string Describe(
        object native,
        IComplexProperty property
    )
        => NestedSetComplexEntryAccess.State(native)
            + "@"
            + NestedSetComplexEntryAccess.Ordinal(native, false)
            + "/"
            + NestedSetComplexEntryAccess.Ordinal(native, true)
            + string.Concat(
                property
                    .ComplexType
                    .GetComplexProperties()
                    .Where(nested => nested.IsCollection)
                    .Select(nested => Lanes(native, nested)));

    /// <summary>Gets a first-level original element entry through EF's public API.</summary>
    private static ComplexElementEntry Original(
        EntityEntry<Payload> entry,
        int ordinal
    )
        => entry
            .ComplexCollection(nameof(Payload.Rows))
            .GetOriginalEntry(ordinal);

    /// <summary>Gets a second-level current element entry through EF's public API.</summary>
    private static ComplexElementEntry Current(
        EntityEntry<Payload> entry,
        int outer,
        int middle
    )
        => entry
            .ComplexCollection(nameof(Payload.Rows))[outer]
            .ComplexCollection(nameof(Outer.Middles))[middle];
}
