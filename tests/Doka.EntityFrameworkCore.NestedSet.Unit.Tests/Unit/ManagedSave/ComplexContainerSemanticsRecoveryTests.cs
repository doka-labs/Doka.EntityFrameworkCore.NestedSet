using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks that recovery keeps caller collection code, Equals and nullable structs out of its path.</summary>
public sealed class ComplexContainerSemanticsRecoveryTests
{
    /// <summary>Stages a field-mapped validating collection without ever calling its mutators.</summary>
    /// <param name="detectAfterRemoval">Whether EF observes the raw removal before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FieldMappedCollectionIsNeverMutatedByStaging(
        bool detectAfterRemoval
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var rows = new GuardedRows { first };
        var payload = new GuardedPayload
        {
            Id = 1,
            Rows = rows,
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var added = new Row { Text = "callback" };
        rows.Add(added);
        context.ChangeTracker.DetectChanges();
        rows.Remove(added);

        if (detectAfterRemoval)
        {
            context.ChangeTracker.DetectChanges();
        }

        rows.Mutations = 0;

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(rows, payload.Rows);
        Assert.Same(first, Assert.Single(rows));
        Assert.Equal(0, rows.Mutations);
        Assert.Equal(
            EntityState.Unchanged,
            entry
                .ComplexCollection(nameof(GuardedPayload.Rows))[0].State);
    }

    /// <summary>Observes no temporary membership through CollectionChanged during field-mapped staging.</summary>
    [Fact]
    public async Task FieldMappedObservableCollectionRaisesOnlyRestoringEvents()
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var rows = new ObservableCollection<Row> { first };
        var payload = new ObservablePayload
        {
            Id = 2,
            Rows = rows,
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var added = new Row { Text = "callback" };
        rows.Add(added);
        context.ChangeTracker.DetectChanges();
        rows.Remove(added);
        var observed = new List<Row?>();
        rows.CollectionChanged += (_, change) =>
        {
            if (change.NewItems is not null)
            {
                observed.AddRange(change.NewItems.Cast<Row?>());
            }
        };

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(first, Assert.Single(rows));
        Assert.DoesNotContain(null, observed);
        Assert.DoesNotContain(added, observed);
    }

    /// <summary>Replays a struct whose Equals ignores an unmapped member the callback changed.</summary>
    [Fact]
    public async Task StructWithCustomEqualsIsReplayedWithoutConsultingIt()
    {
        // Arrange
        await using var context = new SemanticsContext();
        var payload = new EqualityPayload
        {
            Id = 3,
            Value = new EqualityValue
            {
                Key = 1,
                Note = 5,
            },
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Value = new EqualityValue
        {
            Key = 1,
            Note = 9,
        };

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(5, payload.Value.Note);
    }

    /// <summary>Restores an in-place primitive change inside a nullable struct, which EF cannot set directly.</summary>
    /// <param name="detectCallback">Whether EF observes the in-place change before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullableStructRestoresInPlacePrimitiveChange(
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var tags = new List<string> { "stored" };
        var payload = new NullablePayload
        {
            Id = 4,
            Value = new NullableValue
            {
                Count = 1,
                Tags = tags,
            },
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        tags.Add("callback");

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(["stored"], payload.Value!.Value.Tags);
        Assert.False(
            entry
                .ComplexProperty(nameof(NullablePayload.Value))
                .Property(nameof(NullableValue.Tags))
                .IsModified);
    }

    /// <summary>Restores a nullable struct scalar on an element hidden from the current CLR path.</summary>
    [Fact]
    public async Task HiddenElementRestoresNullableStructThroughItsRetainedInstance()
    {
        // Arrange
        await using var context = new SemanticsContext();
        var row = new NullableRow
        {
            Value = new NullableValue
            {
                Count = 1,
                Tags = ["stored"],
            },
        };

        var payload = new NullableRowsPayload
        {
            Id = 5,
            Rows = [row],
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Rows.Clear();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        row.Value!.Value.Tags!.Add("callback");
        payload.Rows.Add(
            new NullableRow
            {
                Value = new NullableValue
                {
                    Count = 2,
                    Tags = [],
                },
            });

        context.ChangeTracker.DetectChanges();
        payload.Rows.Clear();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Empty(payload.Rows);
        Assert.Equal(["stored"], row.Value!.Value.Tags);
    }

    /// <summary>Restores scalars of a raw element that EF never registered, including its nested values.</summary>
    [Fact]
    public async Task UnregisteredElementRestoresItsScalars()
    {
        // Arrange
        await using var context = new SemanticsContext();
        var raw = new NullableRow
        {
            Value = new NullableValue
            {
                Count = 1,
                Tags = ["stored"],
            },
        };

        var payload = new NullableRowsPayload
        {
            Id = 6,
            Rows = [],
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Rows.Add(raw);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var tags = raw.Value.Value.Tags!;
        tags.Add("callback");
        raw.Value = new NullableValue
        {
            Count = 9,
            Tags = tags,
        };

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(raw, Assert.Single(payload.Rows));
        Assert.Equal(1, raw.Value!.Value.Count);
        Assert.Equal(["stored"], raw.Value!.Value.Tags);
    }

    /// <summary>Stages a property-mapped validating collection without ever handing it a null slot.</summary>
    /// <param name="keepFirst">Whether an existing element can fill a vacant slot.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PropertyMappedCollectionNeverReceivesNull(
        bool keepFirst
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var rows = new GuardedRows();

        if (keepFirst)
        {
            rows.Add(first);
        }

        var payload = new PropertyGuardedPayload
        {
            Id = 7,
            Rows = rows,
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var added = new Row { Text = "callback" };
        rows.Add(added);
        context.ChangeTracker.DetectChanges();
        rows.Remove(added);

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(rows, payload.Rows);
        Assert.Equal(
            keepFirst
                ? [first]
                : [],
            rows.ToArray());
    }

    /// <summary>Installs exact membership when a callback changed a read-only view's hidden backing list.</summary>
    /// <param name="detectCallback">Whether EF observes the change before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyViewWithChangedBackingRestoresMembership(
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var backing = new List<Row> { first };
        var payload = new ViewPayload
        {
            Id = 8,
            Rows = new ReadOnlyCollection<Row>(backing),
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        backing.Clear();
        backing.Add(new Row { Text = "callback" });

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(first, Assert.Single(payload.Rows));
        Assert.Equal(
            EntityState.Unchanged,
            entry
                .ComplexCollection(nameof(ViewPayload.Rows))[0].State);
    }

    /// <summary>Accepts the setter's copy when a callback replaced a collection behind a copying setter.</summary>
    /// <param name="detectCallback">Whether EF observes the replacement before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyingSetterRestoresMembershipAfterReplacement(
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var payload = new CopyingPayload
        {
            Id = 9,
            Rows = [first],
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Rows = [new Row { Text = "callback" }];

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(first, Assert.Single(payload.Rows));
        Assert.Equal(
            EntityState.Unchanged,
            entry
                .ComplexCollection(nameof(CopyingPayload.Rows))[0].State);
    }

    /// <summary>Maps each container semantics with automatic detection off, as a suspended callback sees it.</summary>
    private sealed class SemanticsContext : DbContext
    {
        /// <summary>Prevents observation from implicitly accepting callback changes.</summary>
        internal SemanticsContext()
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
            modelBuilder
                .Entity<GuardedPayload>()
                .ComplexCollection(value => value.Rows, rows => rows.ToJson("Rows"));

            modelBuilder
                .Entity<ObservablePayload>()
                .ComplexCollection(value => value.Rows, rows => rows.ToJson("Rows"));

            modelBuilder
                .Entity<EqualityPayload>()
                .ComplexProperty(value => value.Value);

            modelBuilder
                .Entity<NullablePayload>()
                .ComplexProperty(
                    value => value.Value,
                    value => value.PrimitiveCollection(item => item.Tags));

            modelBuilder
                .Entity<NullableRowsPayload>()
                .ComplexCollection(
                    value => value.Rows,
                    rows =>
                    {
                        rows.ToJson("Rows");
                        rows.ComplexProperty(row => row.Value, value => value.PrimitiveCollection(item => item.Tags));
                    });

            modelBuilder
                .Entity<PropertyGuardedPayload>()
                .ComplexCollection(
                    value => value.Rows,
                    rows =>
                    {
                        rows.ToJson("Rows");
                        rows.UsePropertyAccessMode(PropertyAccessMode.Property);
                    });

            modelBuilder
                .Entity<ViewPayload>()
                .ComplexCollection(value => value.Rows, rows => rows.ToJson("Rows"));

            modelBuilder
                .Entity<CopyingPayload>()
                .ComplexCollection(
                    value => value.Rows,
                    rows =>
                    {
                        rows.ToJson("Rows");
                        rows.UsePropertyAccessMode(PropertyAccessMode.Property);
                    });
        }
    }

    /// <summary>Rejects null membership and counts every caller-visible mutation.</summary>
    private sealed class GuardedRows : Collection<Row>
    {
        /// <summary>Gets or sets the number of mutator calls since the last reset.</summary>
        public int Mutations { get; set; }

        /// <inheritdoc />
        protected override void InsertItem(
            int index,
            Row item
        )
        {
            ArgumentNullException.ThrowIfNull(item);
            Mutations++;
            base.InsertItem(index, item);
        }

        /// <inheritdoc />
        protected override void ClearItems()
        {
            Mutations++;
            base.ClearItems();
        }
    }

    /// <summary>Provides a complex collection element.</summary>
    private sealed class Row
    {
        /// <summary>Gets or sets element payload.</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Holds a validating collection behind an auto-property's backing field.</summary>
    private sealed class GuardedPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the field-mapped collection.</summary>
        public GuardedRows Rows { get; set; } = [];
    }

    /// <summary>Holds a validating collection behind a property-mode setter.</summary>
    private sealed class PropertyGuardedPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the property-mapped collection.</summary>
        public GuardedRows Rows { get; set; } = [];
    }

    /// <summary>Declares a mutable collection whose runtime value can be a read-only view.</summary>
    private sealed class ViewPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the mapped collection.</summary>
        public IList<Row> Rows { get; set; } = [];
    }

    /// <summary>Copies every assigned list, so assigning can never keep an instance.</summary>
    private sealed class CopyingPayload
    {
        private List<Row> _store = [];

        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets the stored list or copies an assigned one.</summary>
        public List<Row> Rows
        {
            get => _store;
            set => _store = [.. value];
        }
    }

    /// <summary>Holds an observable collection behind an auto-property's backing field.</summary>
    private sealed class ObservablePayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the field-mapped collection.</summary>
        public ObservableCollection<Row> Rows { get; set; } = [];
    }

    /// <summary>Holds a struct whose equality deliberately ignores one member.</summary>
    private sealed class EqualityPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the value-type complex reference.</summary>
        public EqualityValue Value { get; set; }
    }

    /// <summary>Compares only its mapped key.</summary>
    private struct EqualityValue : IEquatable<EqualityValue>
    {
        /// <summary>Gets or sets the mapped scalar.</summary>
        public int Key { get; set; }

        /// <summary>Gets or sets an unmapped member that recovery still treats as part of the box.</summary>
        [NotMapped]
        public int Note { get; set; }

        /// <inheritdoc />
        public readonly bool Equals(
            EqualityValue other
        ) => Key == other.Key;

        /// <inheritdoc />
        public override readonly bool Equals(
            object? obj
        ) => obj is EqualityValue other && Equals(other);

        /// <inheritdoc />
        public override readonly int GetHashCode() => Key;
    }

    /// <summary>Holds a nullable struct reference.</summary>
    private sealed class NullablePayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the nullable value-type complex reference.</summary>
        public NullableValue? Value { get; set; }
    }

    /// <summary>Holds collection elements that contain nullable struct references.</summary>
    private sealed class NullableRowsPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the complex collection.</summary>
        public List<NullableRow> Rows { get; set; } = [];
    }

    /// <summary>Contains a nullable struct reference.</summary>
    private sealed class NullableRow
    {
        /// <summary>Gets or sets the nullable value-type complex reference.</summary>
        public NullableValue? Value { get; set; }
    }

    /// <summary>Provides a scalar and a mutable primitive collection.</summary>
    private struct NullableValue
    {
        /// <summary>Gets or sets the mapped scalar.</summary>
        public int Count { get; set; }

        /// <summary>Gets or sets the mapped primitive collection.</summary>
        public List<string>? Tags { get; set; }
    }
}
