using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects relationship completion's no-FK path independently of model and native-entry setup.</summary>
public sealed class CollectionBaselineCompletionTests
{
    /// <summary>Completes every supported producer without altering native identities or relationships.</summary>
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
    public async Task RecordSavedRelationships_NoRelationshipCompletion_PreservesNativeIdentities(
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

        // WHY: Pin non-null object keys; comparer inference would otherwise select object?.
        var entities = nodes.ToHashSet<object>(ReferenceEqualityComparer.Instance);

        var writes = new NestedSetCallbackWrites();
        // Act
        writes.RecordSavedRelationships(source, entries, entities);

        // Assert
        Assert.All(identities, identity => Assert.False(identity.HasCollectionRelationships));
        Assert.Equal(
            rows,
            context
                .ChangeTracker
                .Entries()
                .Count());

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
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

    /// <summary>Models a saved input without introducing relationships.</summary>
    private sealed class NoRelationshipsNode
    {
        /// <summary>Gets or sets the fixed input identity.</summary>
        public int Id { get; set; }
    }
}
