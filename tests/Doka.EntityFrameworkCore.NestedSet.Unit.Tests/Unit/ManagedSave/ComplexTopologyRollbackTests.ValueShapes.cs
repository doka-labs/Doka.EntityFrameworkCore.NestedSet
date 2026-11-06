using System.Collections;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Fixed arrays and mutable lists retain exact storage, elements and native ordinals.</summary>
    /// <param name="fixedSize">Whether the caller's runtime collection is an array instead of a mutable List.</param>
    /// <param name="detectCallback">Whether EF detects the replacement collection before recovery.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RuntimeCollectionRestoresExactTopology(
        bool fixedSize,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new ValueShapeContext();
        var first = new TopologyItem
        {
            Text = "Stored collection first",
            Leaves = [new TopologyLeaf()],
        };

        var last = new TopologyItem
        {
            Text = "Stored collection last",
            Leaves = [new TopologyLeaf()],
        };

        IList<TopologyItem> values = fixedSize
            ? new[] { first, last }
            : new List<TopologyItem>
            {
                first,
                last,
            };

        var payload = new CollectionPayload
        {
            Id = 7,
            Rows = values,
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        first.Text = "Pending collection first";
        var collection = entry.ComplexCollection(nameof(CollectionPayload.Rows));
        var before = values
            .Select((item, index) =>
            {
                var element = collection[index];

                return new ItemObservation(
                    item,
                    element.State,
                    ObserveScalar(element.Property(nameof(TopologyItem.Text))),
                    ObserveDetails(element.ComplexProperty(nameof(TopologyItem.Details)), item.Details),
                    ObserveLeaves(element.ComplexCollection(nameof(TopologyItem.Leaves)), item.Leaves));
            })
            .ToArray();

        var collectionModified = collection.IsModified;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Rows = [];

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(typeof(IList<TopologyItem>), collection.Metadata.ClrType);
        Assert.Equal(fixedSize, ((IList)payload.Rows).IsFixedSize);

        if (fixedSize)
        {
            Assert.IsType<TopologyItem[]>(payload.Rows);
        }
        else
        {
            Assert.IsType<List<TopologyItem>>(payload.Rows);
        }

        Assert.Same(values, payload.Rows);
        Assert.Equal(before.Length, payload.Rows.Count);
        Assert.Equal(collectionModified, collection.IsModified);
        Assert.Equal(EntityState.Unchanged, entry.State);

        // WHY: Match original CLR slots to native state for both mutable and fixed-size collection storage.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Length; index++)
        {
            var expected = before[index];
            var element = collection[index];

            Assert.Same(expected.Instance, payload.Rows[index]);
            Assert.Equal(expected.State, element.State);
            AssertScalar(element.Property(nameof(TopologyItem.Text)), expected.Text);
            AssertDetails(
                element.ComplexProperty(nameof(TopologyItem.Details)),
                payload.Rows[index].Details,
                expected.Details);

            AssertLeaves(
                element.ComplexCollection(nameof(TopologyItem.Leaves)),
                payload.Rows[index].Leaves,
                expected.Leaves);
        }

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A struct complex reference restores its value and caller-owned nested list.</summary>
    /// <param name="detectCallback">Whether EF detects the replacement struct before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructReferenceRestoresNestedTopology(
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new ValueShapeContext();
        var leaves = new List<TopologyLeaf> { new() { Text = "Stored struct leaf" } };
        var payload = new StructPayload
        {
            Id = 7,
            Data = new StructValue
            {
                Text = "Stored struct",
                Leaves = leaves,
            },
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Data = new StructValue
        {
            Text = "Pending struct",
            Leaves = leaves,
        };

        leaves[0].Text = "Pending struct leaf";
        var reference = entry.ComplexProperty(nameof(StructPayload.Data));
        var before = ObserveScalar(reference.Property(nameof(StructValue.Text)));
        var nested = ObserveLeaves(reference.ComplexCollection(nameof(StructValue.Leaves)), leaves);
        var referenceModified = reference.IsModified;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Data = new StructValue
        {
            Text = "Callback struct",
            Leaves = [],
        };

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.True(reference.Metadata.ComplexType.ClrType.IsValueType);
        Assert.False(reference.Metadata.IsCollection);
        Assert.Equal("Pending struct", payload.Data.Text);
        Assert.Equal("Stored struct", before.Original);
        AssertScalar(reference.Property(nameof(StructValue.Text)), before);
        Assert.Equal(referenceModified, reference.IsModified);
        AssertLeaves(reference.ComplexCollection(nameof(StructValue.Leaves)), payload.Data.Leaves, nested);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Validates actual collection and struct-reference metadata without database commands.</summary>
    private sealed class ValueShapeContext : DbContext
    {
        /// <summary>Keeps raw pending values independent of original and modified state.</summary>
        internal ValueShapeContext()
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
            var collection = modelBuilder.Entity<CollectionPayload>();
            collection
                .Property(value => value.Id)
                .ValueGeneratedNever();

            collection.ComplexCollection(
                value => value.Rows,
                rows =>
                {
                    rows.ToJson("Rows");
                    rows
                        .ComplexProperty(value => value.Details)
                        .IsRequired(false);

                    rows.ComplexCollection(value => value.Leaves);
                });

            var structure = modelBuilder.Entity<StructPayload>();
            structure
                .Property(value => value.Id)
                .ValueGeneratedNever();

            structure.ComplexProperty(
                value => value.Data,
                data =>
                {
                    data.ToJson("Data");
                    data.ComplexCollection(value => value.Leaves);
                });
        }
    }

    /// <summary>Accepts caller-owned fixed-size arrays and mutable lists through the same mapped interface.</summary>
    private sealed class CollectionPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the runtime array or list independently of EF's original collection.</summary>

        // WHY: EF snapshots using the declared collection type. IList allows a mutable original snapshot
        // while callers retain either a fixed-size runtime array or a mutable runtime List instance.
        public IList<TopologyItem> Rows { get; set; } = [];
    }

    /// <summary>Contains a value-type complex reference with class-based nested collection elements.</summary>
    private sealed class StructPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the supported struct complex reference.</summary>
        public StructValue Data { get; set; }
    }

    /// <summary>Is a complex reference value, never an unsupported value-type collection element.</summary>
    private struct StructValue
    {
        /// <summary>Gets or sets the value-type complex scalar.</summary>
        public string Text { get; set; }

        /// <summary>Gets or sets caller-owned reference storage nested inside the value-type complex.</summary>
        public List<TopologyLeaf>? Leaves { get; set; }
    }
}
