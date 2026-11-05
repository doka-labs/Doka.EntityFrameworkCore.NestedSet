namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Restores an optional JSON document and native children after callback removal or replacement.</summary>
    /// <param name="replace">Whether the callback replaces the document instead of setting it null.</param>
    /// <param name="detectCallback">Whether EF detects the callback's new topology.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OptionalDocumentRestoresCollectionContainers(
        bool replace,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new OptionalCollectionContext();
        var payload = CreateOptionalPayload();
        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var document = payload.Document!;
        payload.Marker = "Pending optional marker";
        document.Title = "Pending document";
        document.Children![0].Text = "Pending document child";
        var before = ObserveDocument(entry, originalCount: 2);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Document = replace
            ? CreateCallbackDocument()
            : null;

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // WHY: EF may reuse existing native ordinal entries when their optional container changes. Those
        // entries belong to the captured preimage; only genuinely Added callback handles should be retired.
        var introduced = replace && detectCallback
            ? ReadDocumentEntries(entry, count: 2)
                .Where(element => element.State == EntityState.Added)
                .ToArray()
            : [];

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, before.State);
        Assert.Equal("Pending document", before.Title.Current);
        Assert.Equal("Stored document", before.Title.Original);
        AssertDocument(entry, before);
        Assert.All(introduced, element => Assert.Equal(EntityState.Detached, element.State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Introduced nested entries are retired when the captured optional document returns to null.</summary>
    /// <param name="literalNull">Whether null was originally tracked or set raw over a stored document.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullDocumentRetiresIntroducedNativeChildren(
        bool literalNull
    )
    {
        // Arrange
        await using var context = new OptionalCollectionContext();
        var payload = CreateOptionalPayload();

        if (literalNull)
        {
            payload.Document = null;
        }

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Document = null;
        var before = ObserveDocument(
            entry,
            originalCount: literalNull
                ? 0
                : 2);

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Document = CreateCallbackDocument();
        context.ChangeTracker.DetectChanges();
        var callbackEntries = ReadDocumentEntries(entry, count: 2);
        var callbackStates = callbackEntries
            .Select(element => element.State)
            .ToArray();

        var introduced = callbackEntries
            .Where(element => element.State == EntityState.Added)
            .ToArray();

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Null(before.Instance);
        Assert.Null(before.Children);
        Assert.Equal(
            literalNull
                ? null
                : "Stored document",
            before.Title.Original);

        if (literalNull)
        {
            // WHY: A literal-null original has no native entries to reuse. Require both actual additions so
            // retirement cannot pass vacuously after the optional reference is hidden again.
            Assert.Equal(2, introduced.Length);
            Assert.All(callbackStates, state => Assert.Equal(EntityState.Added, state));
        }

        AssertDocument(entry, before);
        Assert.All(introduced, element => Assert.Equal(EntityState.Detached, element.State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A hidden modified child retains its original buffer, flag and raw CLR value after rollback.</summary>
    /// <param name="accepted">Whether EF accepted the callback's replacement document before rollback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenModifiedDocumentChildrenRetainFlagsAfterAcceptance(
        bool accepted
    )
    {
        // Arrange
        await using var context = new OptionalCollectionContext();
        var payload = CreateOptionalPayload();
        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var originalDocument = payload.Document!;
        var originalChildren = originalDocument.Children!;
        var originalChild = originalChildren[0];
        var collection = entry
            .ComplexProperty(value => value.Document)
            .ComplexCollection(nameof(OptionalDocument.Children));

        // WHY: Keep the actual native handles before hiding their CLR parent. Under null, the current getter
        // cannot read that path, but the original buffer and modified flag still belong to the pending write.
        var originalEntry = collection.GetOriginalEntry(0);
        var originalProperty = originalEntry.Property(nameof(OptionalChild.Text));
        originalChild.Text = "Pending hidden child";
        originalProperty.IsModified = true;
        var originalValue = originalProperty.OriginalValue;
        payload.Document = null;
        var before = ObserveDocument(entry, originalCount: 2);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Document = CreateCallbackDocument();
        context.ChangeTracker.DetectChanges();

        if (accepted)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Modified, before.State);
        Assert.Equal(EntityState.Modified, before.Originals[0].State);
        Assert.True(before.Originals[0].Modified);
        Assert.Equal("Stored child first", originalValue);
        AssertDocument(entry, before);
        Assert.Null(payload.Document);
        Assert.Equal(EntityState.Modified, originalEntry.State);
        Assert.True(originalProperty.IsModified);
        Assert.Equal(originalValue, originalProperty.OriginalValue);
        Assert.Same(originalChildren, originalDocument.Children);
        Assert.Same(originalChild, originalDocument.Children![0]);
        Assert.Equal("Pending hidden child", originalChild.Text);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A captured raw removal retains its native tail through callback acceptance.</summary>
    /// <param name="accepted">Whether EF accepted the later callback's empty collection before rollback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawOuterRemovalPreservesNativeTailAfterCallback(
        bool accepted
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackGraph(context);
        entry.Entity.Rows!.RemoveAt(1);
        var before = Observe(entry);
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var originals = Enumerable
            .Range(0, 2)
            .Select(index => ObserveOriginal(collection.GetOriginalEntry(index)))
            .ToArray();

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entry.Entity.Rows!.Clear();
        context.ChangeTracker.DetectChanges();

        if (accepted)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, before.State);
        Assert.Single(before.Items);
        Assert.Equal("Stored last", originals[1].Value);
        AssertGraph(entry, before);

        // WHY: A raw pre-save removal leaves a real native tail beyond the current CLR count. It must not be
        // detected or discarded, and reading its current CLR value would itself be an invalid ordinal access.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < originals.Length; index++)
        {
            AssertOriginal(collection.GetOriginalEntry(index), originals[index]);
        }

        AssertSettings(context);
    }

    /// <summary>Observes optional reference identity separately from current and original native lanes.</summary>
    private static DocumentObservation ObserveDocument(
        EntityEntry<OptionalCollectionPayload> entry,
        int originalCount
    )
    {
        var reference = entry.ComplexProperty(value => value.Document);
        var collection = reference.ComplexCollection(nameof(OptionalDocument.Children));
        var instance = entry.Entity.Document;
        var children = instance?.Children;
        var current = children is null
            ? []
            : children
                .Select((child, index) => new DocumentChildObservation(
                    child,
                    collection[index].State,
                    ObserveScalar(
                        collection[index]
                            .Property(nameof(OptionalChild.Text)))))
                .ToArray();

        // WHY: Cached native entries may remain installed when a raw-null optional container hides their CLR path.
        // State and original buffers are observable without invoking their unavailable current CLR scalar getter.
        var installed = Enumerable
            .Range(0, originalCount)
            .Select(index => collection[index].State)
            .ToArray();

        var originals = Enumerable
            .Range(0, originalCount)
            .Select(index => ObserveOriginal(collection.GetOriginalEntry(index)))
            .ToArray();

        return new DocumentObservation(
            entry.State,
            ObserveScalar(entry.Property(value => value.Marker)),
            instance,
            ObserveScalar(reference.Property(nameof(OptionalDocument.Title))),
            reference.IsModified,
            children,
            collection.IsModified,
            current,
            installed,
            originals);
    }

    /// <summary>Verifies literal null, exact containers and native lanes before any further change detection.</summary>
    private static void AssertDocument(
        EntityEntry<OptionalCollectionPayload> entry,
        DocumentObservation before
    )
    {
        Assert.Equal(before.State, entry.State);
        AssertScalar(entry.Property(value => value.Marker), before.Marker);
        Assert.Same(before.Instance, entry.Entity.Document);
        var reference = entry.ComplexProperty(value => value.Document);
        var collection = reference.ComplexCollection(nameof(OptionalDocument.Children));

        Assert.Equal(before.ReferenceModified, reference.IsModified);
        AssertScalar(reference.Property(nameof(OptionalDocument.Title)), before.Title);
        Assert.Same(before.Children, entry.Entity.Document?.Children);
        Assert.Equal(before.Current.Length, entry.Entity.Document?.Children?.Count ?? 0);
        Assert.Equal(before.CollectionModified, collection.IsModified);

        // WHY: Current CLR members and installed native slots are distinct when the captured parent is raw-null.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Current.Length; index++)
        {
            var expected = before.Current[index];
            var element = collection[index];
            var actual = entry.Entity.Document!.Children![index];

            Assert.Same(expected.Instance, actual);
            Assert.Equal(expected.State, element.State);
            AssertScalar(element.Property(nameof(OptionalChild.Text)), expected.Text);
            Assert.Equal(expected.Text.Current, actual.Text);
        }

        // WHY: Original-only observations avoid pretending that a hidden CLR element has a readable current value.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Originals.Length; index++)
        {
            Assert.Equal(before.InstalledStates[index], collection[index].State);
            AssertOriginal(collection.GetOriginalEntry(index), before.Originals[index]);
        }

        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);
    }

    /// <summary>Reads an existing original buffer and flag without requiring a current CLR path.</summary>
    private static OriginalChildObservation ObserveOriginal(
        ComplexElementEntry element
    )
    {
        var property = element.Property("Text");

        return new OriginalChildObservation(element.State, property.OriginalValue, property.IsModified);
    }

    /// <summary>Checks original native identity roles after callback deletion or accepted slot retirement.</summary>
    private static void AssertOriginal(
        ComplexElementEntry element,
        OriginalChildObservation before
    )
    {
        var property = element.Property("Text");

        Assert.Equal(before.State, element.State);
        Assert.Equal(before.Value, property.OriginalValue);
        Assert.Equal(before.Modified, property.IsModified);
    }

    /// <summary>Retains callback native handles while their CLR container remains reachable.</summary>
    private static ComplexElementEntry[] ReadDocumentEntries(
        EntityEntry<OptionalCollectionPayload> entry,
        int count
    )
    {
        var collection = entry
            .ComplexProperty(value => value.Document)
            .ComplexCollection(nameof(OptionalDocument.Children));

        return Enumerable
            .Range(0, count)
            .Select(index => collection[index])
            .ToArray();
    }

    /// <summary>Creates stored non-null reference and collection topology without database initialization.</summary>
    private static OptionalCollectionPayload CreateOptionalPayload()
        => new()
        {
            Id = 7,
            Document = new OptionalDocument
            {
                Title = "Stored document",
                Children =
                [
                    new OptionalChild { Text = "Stored child first" },
                    new OptionalChild { Text = "Stored child last" },
                ],
            },
        };

    /// <summary>Creates a genuinely new callback container and native child candidates.</summary>
    private static OptionalDocument CreateCallbackDocument()
        => new()
        {
            Title = "Callback document",
            Children =
            [
                new OptionalChild { Text = "Callback child first" },
                new OptionalChild { Text = "Callback child last" },
            ],
        };

    /// <summary>Maps a supported optional JSON complex reference containing a class-based complex collection.</summary>
    private sealed class OptionalCollectionContext : DbContext
    {
        /// <summary>Retains the distinction between raw CLR state and explicitly detected state.</summary>
        internal OptionalCollectionContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

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
            var payload = modelBuilder.Entity<OptionalCollectionPayload>();
            payload
                .Property(value => value.Id)
                .ValueGeneratedNever();

            payload.ComplexProperty(
                value => value.Document,
                document =>
                {
                    document.IsRequired(false);
                    document.ToJson("Document");
                    document.ComplexCollection(value => value.Children);
                });
        }
    }

    /// <summary>Contains independent scalar payload and optional JSON graph storage.</summary>
    private sealed class OptionalCollectionPayload
    {
        /// <summary>Gets or sets the fixed tracked identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets payload whose raw value and original are independent of the document.</summary>
        public string Marker { get; set; } = "Stored optional marker";

        /// <summary>Gets or sets the optional complex container holding the child collection.</summary>
        public OptionalDocument? Document { get; set; }
    }

    /// <summary>Provides a required existence marker and independently nullable nested list.</summary>
    private sealed class OptionalDocument
    {
        /// <summary>Gets or sets the existence-determining scalar within the optional reference.</summary>
        public string Title { get; set; } = "";

        /// <summary>Gets or sets the exact child list retained across recovery.</summary>
        public List<OptionalChild>? Children { get; set; }
    }

    /// <summary>Provides one observable nested complex scalar and native element state.</summary>
    private sealed class OptionalChild
    {
        /// <summary>Gets or sets child payload with an independent original buffer.</summary>
        public string Text { get; set; } = "";
    }

    /// <summary>Retains optional topology separately from installed native slots beneath a null CLR path.</summary>
    private sealed record DocumentObservation(
        EntityState State,
        ScalarObservation Marker,
        OptionalDocument? Instance,
        ScalarObservation Title,
        bool ReferenceModified,
        List<OptionalChild>? Children,
        bool CollectionModified,
        DocumentChildObservation[] Current,
        EntityState[] InstalledStates,
        OriginalChildObservation[] Originals
    );

    /// <summary>Retains one visible current child separately from its scalar original and modification flag.</summary>
    private sealed record DocumentChildObservation(
        OptionalChild Instance,
        EntityState State,
        ScalarObservation Text
    );

    /// <summary>Retains native original-only state without reading an unavailable current CLR value.</summary>
    private sealed record OriginalChildObservation(
        EntityState State,
        object? Value,
        bool Modified
    );
}
