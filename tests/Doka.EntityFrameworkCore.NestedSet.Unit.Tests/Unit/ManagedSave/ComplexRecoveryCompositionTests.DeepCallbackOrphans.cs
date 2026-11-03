namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexRecoveryCompositionTests
{
    /// <summary>Restores four callback-only complex levels even when their root CLR collection disappears.</summary>
    /// <param name="capturedNull">Whether the captured root collection is null rather than empty.</param>
    /// <param name="removeRoot">Whether the callback replaces its root with null rather than clearing the list.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeepCallbackOnlyOwnersRestoreAbsentCapturedMembership(
        bool capturedNull,
        bool removeRoot
    )
    {
        // Arrange
        await using var context = new CallbackOnlyContext();
        var payload = new CallbackOnlyPayload
        {
            Id = 61,
            Rows = capturedNull ? null : [],
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Marker = "Pending deep-root marker";
        entry.Property(value => value.Marker).IsModified = true;
        var beforeRows = payload.Rows;
        var before = ObserveDeep(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [Write(context, payload)]);
        var captured = ObserveDeep(entry);
        payload.Rows ??= [];
        payload.Rows.Add(new CallbackOnlyLevel
        {
            Branches =
            [
                new CallbackOnlyMiddle
                {
                    Segments = [new CallbackOnlyTip { Leaves = [new CallbackOnlyLeaf()] }],
                },
            ],
        });
        context.ChangeTracker.DetectChanges();
        var outer = entry.ComplexCollection(nameof(CallbackOnlyPayload.Rows))[0];

        var middle = outer.ComplexCollection(nameof(CallbackOnlyLevel.Branches))[0];

        var tip = middle.ComplexCollection(nameof(CallbackOnlyMiddle.Segments))[0];

        var leaf = tip.ComplexCollection(nameof(CallbackOnlyTip.Leaves))[0];

        ComplexElementEntry[] callbackEntries = [outer, middle, tip, leaf];
        var callbackStates = callbackEntries
            .Select(element => element.State)
            .ToArray();

        var callbackIdentities = CallbackIdentities(callbackEntries);

        if (removeRoot)
        {
            payload.Rows = null;
        }
        else
        {
            payload.Rows.Clear();
        }

        output.WriteLine($"Callback complex depth=4; capturedNull={capturedNull}; removeRoot={removeRoot}.");

        // Act
        snapshot.Restore();
        snapshot.Restore();
        var restored = ObserveDeep(entry);
        context.ChangeTracker.DetectChanges();

        // Assert
        Assert.All(callbackStates, state => Assert.Equal(EntityState.Added, state));
        AssertNative(before, captured, exactLanes: false);
        AssertNative(captured, restored, exactLanes: true);
        AssertNative(captured, ObserveDeep(entry), exactLanes: true);
        AssertUnregisteredCallbacks(callbackIdentities, ObserveDeep(entry));
        Assert.Same(beforeRows, payload.Rows);

        if (!capturedNull)
        {
            Assert.Empty(payload.Rows!);
        }

        Assert.Equal("Pending deep-root marker", payload.Marker);
        Assert.Equal("Stored deep-root marker",
            entry.Property(value => value.Marker).OriginalValue);
        Assert.True(entry.Property(value => value.Marker).IsModified);
        Assert.False(entry.ComplexCollection(nameof(CallbackOnlyPayload.Rows)).IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Persists the restored write without resurrecting four callback-only complex levels.</summary>
    /// <param name="capturedNull">Whether the database and captured root membership are null rather than empty.</param>
    /// <param name="removeRoot">Whether the callback replaces its root with null rather than clearing the list.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeepCallbackOnlyOwnersAllowActualSqliteRetry(
        bool capturedNull,
        bool removeRoot
    )
    {
        // Arrange
        await using var context = new CallbackOnlyContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var payload = new CallbackOnlyPayload
        {
            Id = 67,
            Rows = capturedNull ? null : [],
        };

        var entry = context.Add(payload);
        await context.SaveChangesAsync(CancellationToken.None);
        payload.Marker = "Pending deep-root marker";
        entry.Property(value => value.Marker).IsModified = true;
        var beforeRows = payload.Rows;
        var before = ObserveDeep(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [Write(context, payload)]);
        var captured = ObserveDeep(entry);
        payload.Rows ??= [];
        payload.Rows.Add(new CallbackOnlyLevel
        {
            Branches =
            [
                new CallbackOnlyMiddle
                {
                    Segments = [new CallbackOnlyTip { Leaves = [new CallbackOnlyLeaf()] }],
                },
            ],
        });
        context.ChangeTracker.DetectChanges();
        var outer = entry.ComplexCollection(nameof(CallbackOnlyPayload.Rows))[0];

        var middle = outer.ComplexCollection(nameof(CallbackOnlyLevel.Branches))[0];

        var tip = middle.ComplexCollection(nameof(CallbackOnlyMiddle.Segments))[0];

        var leaf = tip.ComplexCollection(nameof(CallbackOnlyTip.Leaves))[0];

        ComplexElementEntry[] callbackEntries = [outer, middle, tip, leaf];
        var callbackStates = callbackEntries
            .Select(element => element.State)
            .ToArray();

        var callbackIdentities = CallbackIdentities(callbackEntries);

        if (removeRoot)
        {
            payload.Rows = null;
        }
        else
        {
            payload.Rows.Clear();
        }

        output.WriteLine($"Actual SQLite retry; complex depth=4; capturedNull={capturedNull}; null={removeRoot}.");

        // Act
        snapshot.Restore();
        snapshot.Restore();
        var restored = ObserveDeep(entry);
        var restoredRows = payload.Rows;
        context.ChangeTracker.DetectChanges();
        var detected = ObserveDeep(entry);
        await context.SaveChangesAsync(CancellationToken.None);
        var stored = await context
            .Set<CallbackOnlyPayload>()
            .AsNoTracking()
            .SingleAsync(value => value.Id == payload.Id, CancellationToken.None);

        // Assert
        Assert.All(callbackStates, state => Assert.Equal(EntityState.Added, state));
        AssertNative(before, captured, exactLanes: false);
        AssertNative(captured, restored, exactLanes: true);
        AssertNative(captured, detected, exactLanes: true);
        AssertUnregisteredCallbacks(callbackIdentities, restored);
        AssertUnregisteredCallbacks(callbackIdentities, detected);
        AssertUnregisteredCallbacks(callbackIdentities, ObserveDeep(entry));
        Assert.Same(beforeRows, restoredRows);
        Assert.Equal("Pending deep-root marker", stored.Marker);

        if (capturedNull)
        {
            Assert.Null(stored.Rows);
        }
        else
        {
            Assert.NotNull(stored.Rows);
            Assert.Empty(stored.Rows);
        }

        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        // WHY: This fixture deliberately holds its in-memory database connection open across retry and readback.
        Assert.Equal(ConnectionState.Open,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Observes the deeper mapped fixture without creating or modifying native entries.</summary>
    private static NativeObservation[] ObserveDeep(
        EntityEntry<CallbackOnlyPayload> entry
    )
    {
        var native = new List<NativeObservation>();
        var root = s_memberOwner.GetValue(entry.Property(value => value.Id))!;
        ReadNative(root,
            entry.Metadata,
            "deepPayload",
            native,
            new HashSet<object>(ReferenceEqualityComparer.Instance),
            hasCurrentPath: true);

        return native.ToArray();
    }

    /// <summary>Maps four ordinary class-complex collection levels without opening a database connection.</summary>
    private sealed class CallbackOnlyContext : DbContext
    {
        /// <summary>Leaves callback detection explicit and observable.</summary>
        internal CallbackOnlyContext() => ChangeTracker.AutoDetectChangesEnabled = false;

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
            => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var payload = modelBuilder.Entity<CallbackOnlyPayload>();
            payload
                .Property(value => value.Id)
                .ValueGeneratedNever();
            payload.ComplexCollection(value => value.Rows,
                rows =>
                {
                    rows.ToJson("Rows");
                    rows.ComplexCollection(value => value.Branches,
                        branches =>
                            branches.ComplexCollection(value => value.Segments,
                                segments =>
                                    segments.ComplexCollection(value => value.Leaves)));
                });
        }
    }

    /// <summary>Provides independent pending payload above a nullable callback-only complex collection.</summary>
    private sealed class CallbackOnlyPayload
    {
        /// <summary>Gets or sets the fixed root identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets independent scalar write intent.</summary>
        public string Marker { get; set; } = "Stored deep-root marker";

        /// <summary>Gets or sets root membership that is absent before the callback.</summary>
        public List<CallbackOnlyLevel>? Rows { get; set; }
    }

    /// <summary>Provides the first callback-only native owner.</summary>
    private sealed class CallbackOnlyLevel
    {
        /// <summary>Gets or sets observable first-level scalar payload.</summary>
        public string Text { get; set; } = "Callback outer";

        /// <summary>Gets or sets the second nested complex level.</summary>
        public List<CallbackOnlyMiddle> Branches { get; set; } = [];
    }

    /// <summary>Provides the second callback-only native owner.</summary>
    private sealed class CallbackOnlyMiddle
    {
        /// <summary>Gets or sets observable second-level scalar payload.</summary>
        public string Text { get; set; } = "Callback middle";

        /// <summary>Gets or sets the third nested complex level.</summary>
        public List<CallbackOnlyTip> Segments { get; set; } = [];
    }

    /// <summary>Provides the third callback-only native owner.</summary>
    private sealed class CallbackOnlyTip
    {
        /// <summary>Gets or sets observable third-level scalar payload.</summary>
        public string Text { get; set; } = "Callback tip";

        /// <summary>Gets or sets the fourth nested complex level.</summary>
        public List<CallbackOnlyLeaf> Leaves { get; set; } = [];
    }

    /// <summary>Provides a terminal fourth-level native entry retained after its current CLR path disappears.</summary>
    private sealed class CallbackOnlyLeaf
    {
        /// <summary>Gets or sets observable terminal scalar payload.</summary>
        public string Text { get; set; } = "Callback leaf";
    }
}
