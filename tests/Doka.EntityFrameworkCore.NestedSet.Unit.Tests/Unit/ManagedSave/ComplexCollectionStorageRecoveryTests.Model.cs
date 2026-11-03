namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexCollectionStorageRecoveryTests
{
    /// <summary>Uses actual EF mappings without implicitly detecting pending callback changes.</summary>
    private sealed class StorageContext : DbContext
    {
        /// <summary>Prevents observation from implicitly accepting pending callback changes.</summary>
        internal StorageContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var collection = modelBuilder.Entity<CollectionPayload>();
            collection
                .Property(value => value.Id)
                .ValueGeneratedNever();

            collection.ComplexCollection(value => value.Rows, rows => rows.ToJson("Rows"));

            var copying = modelBuilder.Entity<CopyingPayload>();
            copying
                .Property(value => value.Id)
                .ValueGeneratedNever();

            copying.Ignore(value => value.SetterCalls);
            copying.ComplexCollection(
                value => value.Rows,
                rows =>
                {
                    rows.ToJson("Rows");
                    rows.UsePropertyAccessMode(PropertyAccessMode.Property);
                });

            var structure = modelBuilder.Entity<StructPayload>();
            structure
                .Property(value => value.Id)
                .ValueGeneratedNever();

            structure.ComplexProperty(value => value.Data, ConfigurePrimitive);

            var nested = modelBuilder.Entity<NestedStructPayload>();
            nested
                .Property(value => value.Id)
                .ValueGeneratedNever();

            nested.ComplexProperty(
                value => value.Data,
                outer => outer.ComplexProperty(value => value.Inner, ConfigurePrimitive));

            var structRows = modelBuilder.Entity<StructRowsPayload>();
            structRows
                .Property(value => value.Id)
                .ValueGeneratedNever();

            structRows.ComplexCollection(
                value => value.Rows,
                elements =>
                {
                    elements.ToJson("Rows");
                    elements.ComplexProperty(value => value.Data, ConfigurePrimitive);
                });
        }

        /// <summary>Uses real comparer snapshots for mutable values at either supported struct depth.</summary>
        private static void ConfigurePrimitive(
            ComplexPropertyBuilder<PrimitiveValue> data
        )
        {
            data.PrimitiveCollection(value => value.Tags);
            data
                .Property(value => value.Bytes)
                .Metadata
                .SetValueComparer(
                    new ValueComparer<byte[]>(
                        (left, right) => left != null
                            && right != null
                            && left
                                .AsEnumerable()
                                .SequenceEqual(right),
                        value => value.Aggregate(0, HashCode.Combine),
                        value => value.ToArray()));
        }
    }

    /// <summary>Declares mutable snapshot storage independently of the caller's runtime implementation.</summary>
    private sealed class CollectionPayload
    {
        /// <summary>Gets or sets the fixed entity identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets independent application payload.</summary>
        public string Marker { get; set; } = "Stored marker";

        /// <summary>Gets or sets caller-owned complex collection storage.</summary>
        // WHY: IList lets EF build a mutable original snapshot even when the runtime value cannot be changed.
        public IList<StorageItem> Rows { get; set; } = [];
    }

    /// <summary>Exposes a legal copying setter whose invocation changes caller-owned collection identity.</summary>
    private sealed class CopyingPayload
    {
        private List<StorageItem> _rows = [];

        /// <summary>Gets or sets the fixed entity identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets the number of CLR setter calls independently of EF state.</summary>
        public int SetterCalls { get; private set; }

        /// <summary>Gets the current container or copies an assigned sequence into distinct storage.</summary>
        public List<StorageItem> Rows
        {
            get => _rows;
            set
            {
                SetterCalls++;
                _rows = new List<StorageItem>(value);
            }
        }
    }

    /// <summary>Contains scalar payload in each complex collection element.</summary>
    private sealed class StorageItem
    {
        /// <summary>Gets or sets a scalar whose original and current values can differ.</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Contains a supported struct complex reference with mutable primitive scalar values.</summary>
    private sealed class StructPayload
    {
        /// <summary>Gets or sets the fixed entity identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the value-type complex reference.</summary>
        public PrimitiveValue Data { get; set; }
    }

    /// <summary>Requires primitive scalar recovery to copy values through two containing struct references.</summary>
    private sealed class NestedStructPayload
    {
        /// <summary>Gets or sets the fixed entity identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the outer mapped struct reference.</summary>
        public OuterValue Data { get; set; }
    }

    /// <summary>Retains the inner struct as a value rather than a shared reference.</summary>
    private struct OuterValue
    {
        /// <summary>Gets or sets the mapped inner struct reference.</summary>
        public PrimitiveValue Inner { get; set; }
    }

    /// <summary>Contains class-based complex rows with supported struct references.</summary>
    private sealed class StructRowsPayload
    {
        /// <summary>Gets or sets the fixed entity identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the caller-owned complex collection.</summary>
        public List<StructRow> Rows { get; set; } = [];
    }

    /// <summary>Retains a reference identity even when its nested struct is boxed by an EF getter.</summary>
    private sealed class StructRow
    {
        /// <summary>Gets or sets the supported complex struct reference.</summary>
        public PrimitiveValue Data { get; set; }
    }

    /// <summary>Keeps primitive collections inside a supported struct complex reference.</summary>
    private struct PrimitiveValue
    {
        /// <summary>Gets or sets independently tracked scalar payload.</summary>
        public int Number { get; set; }

        /// <summary>Gets or sets a mutable primitive collection with EF's collection comparer.</summary>
        public List<string> Tags { get; set; }

        /// <summary>Gets or sets byte-array payload with an explicit structural snapshot contract.</summary>
        public byte[] Bytes { get; set; }
    }

    /// <summary>Retains scalar observations without allowing callback mutation to rewrite the oracle.</summary>
    private sealed record ScalarObservation(
        object? Current,
        object? Original,
        bool Modified
    );

    /// <summary>Reads immutable scalar observations without detecting changes.</summary>
    private static ScalarObservation Observe(
        PropertyEntry property
    ) => new(property.CurrentValue, property.OriginalValue, property.IsModified);

    /// <summary>Checks public EF current/original values and modification state independently.</summary>
    private static void AssertScalar(
        PropertyEntry property,
        ScalarObservation expected
    )
    {
        Assert.Equal(expected.Current, property.CurrentValue);
        Assert.Equal(expected.Original, property.OriginalValue);
        Assert.Equal(expected.Modified, property.IsModified);
    }
}
