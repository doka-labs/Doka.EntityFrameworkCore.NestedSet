using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects relationship completion's no-FK path independently of model and native-entry setup.</summary>
[Collection("Allocation measurements")]
public sealed class CollectionBaselineCompletionAllocationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains exact allocation observations without imposing hardware-dependent elapsed-time gates.</summary>
    /// <param name="output">The test output receiving the measured and source-enumeration allocations.</param>
    public CollectionBaselineCompletionAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Creates no batch, candidate list or relationship set before the no-relationship return.</summary>
    /// <param name="shape">The actual array, list, dictionary view or lazy producer representation.</param>
    /// <param name="rows">The number of already-captured identities supplied by that producer.</param>
    [Theory]
    [InlineData("array", 0)]
    [InlineData("array", 1)]
    [InlineData("array", 64)]
    [InlineData("array", 1_024)]
    [InlineData("list", 0)]
    [InlineData("list", 1)]
    [InlineData("list", 64)]
    [InlineData("list", 1_024)]
    [InlineData("values", 0)]
    [InlineData("values", 1)]
    [InlineData("values", 64)]
    [InlineData("values", 1_024)]
    [InlineData("projection", 0)]
    [InlineData("projection", 1)]
    [InlineData("projection", 64)]
    [InlineData("projection", 1_024)]
    [InlineData("flattened", 0)]
    [InlineData("flattened", 1)]
    [InlineData("flattened", 64)]
    [InlineData("flattened", 1_024)]
    [Trait("Category", "Allocation")]
    public async Task NoRelationshipCompletionAvoidsBatchAllocation(
        string shape,
        int rows
    )
    {
        // Arrange
        await using var context = new NoRelationshipsContext();
        var nodes = Enumerable
            .Range(1, rows)
            .Select(id => new NoRelationshipsNode { Id = id })
            .ToArray();

        context.AttachRange(nodes);
        var identities = nodes
            .Select(node => NestedSetInsertionTracking.Capture(context.Entry(node)))
            .ToArray();

        var source = Source(identities, shape);
        var entries = identities
            .Select(identity => identity.UpdateEntry)
            .ToHashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);

        var entities = nodes
            .Cast<object>()
            .ToHashSet(ReferenceEqualityComparer.Instance);

        var writes = new NestedSetCallbackWrites();
        const int warmups = 32;
        const int repetitions = 256;

        // WHY: Prepare shared dispatch and iterator code before measuring the already captured wave.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < warmups; index++)
        {
            writes.RecordSavedRelationships(source, entries, entities);
        }

        var enumeration = MeasureEnumeration(source, repetitions);
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: Repeated completion makes one new per-call batch observable even for a singleton input.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            writes.RecordSavedRelationships(source, entries, entities);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        // WHY: Arrays and lists permit allocation-free indexed visits. Other producers retain their existing
        // enumerator cost; none should acquire an additional batch buffer, set or candidate list.
        var maximum = shape is "array" or "list" ? 4_096 : enumeration.Allocated + 4_096;
        _output.WriteLine(
            $"Saved relationship completion: shape={shape}; rows={rows}; calls={repetitions}; "
            + $"allocated={allocated} B; source enumeration={enumeration.Allocated} B; maximum={maximum} B.");

        Assert.All(identities, identity => Assert.False(identity.HasCollectionRelationships));
        Assert.Equal(rows * repetitions, enumeration.Visits);
        Assert.InRange(allocated, 0, maximum);
        Assert.Equal(rows,
            context
                .ChangeTracker
                .Entries()
                .Count());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Builds source iterators before the interval, as the production callers do.</summary>
    private static IEnumerable<NestedSetInsertionTracking.Identity> Source(
        NestedSetInsertionTracking.Identity[] identities,
        string shape
    ) => shape switch
    {
        "array" => identities,
        "list" => identities.ToList(),
        "values" => identities
            .Select((identity, index) => (identity, index))
            .ToDictionary(item => item.index, item => item.identity)
            .Values,
        "projection" => identities
            .Where(static identity => identity is not null)
            .Select(static identity => identity),
        "flattened" => new[] { identities }.SelectMany(static batch => batch),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Separates source-owned iterator allocations from completion-owned temporary storage.</summary>
    private static (long Allocated, int Visits) MeasureEnumeration(
        IEnumerable<NestedSetInsertionTracking.Identity> source,
        int repetitions
    )
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var visits = 0;

        // WHY: This control traverses the same warmed producer the same number of times as completion.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            foreach (var identity in source)
            {
                GC.KeepAlive(identity);
                visits++;
            }
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before, visits);
    }

    /// <summary>Supplies genuine EF native entries whose metadata has no foreign-key collection work.</summary>
    private sealed class NoRelationshipsContext : DbContext
    {
        /// <summary>Keeps the resource interval free of implicit change detection.</summary>
        internal NoRelationshipsContext()
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
        ) => modelBuilder
            .Entity<NoRelationshipsNode>()
            .Property(node => node.Id)
            .ValueGeneratedNever();
    }

    /// <summary>Models a saved input without introducing unrelated relationship allocation costs.</summary>
    private sealed class NoRelationshipsNode
    {
        /// <summary>Gets or sets the fixed input identity.</summary>
        public int Id { get; set; }
    }
}
