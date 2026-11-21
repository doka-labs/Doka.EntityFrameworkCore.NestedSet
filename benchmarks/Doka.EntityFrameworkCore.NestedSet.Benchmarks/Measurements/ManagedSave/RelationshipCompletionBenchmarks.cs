using Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures actual no-relationship completion across every original producer representation.</summary>
public class RelationshipCompletionBenchmarks
{
    private ManagedSaveContextOwner<CompletionContext>? _owner;
    private NestedSetInsertionTracking.Identity[] _identities = null!;
    private IEnumerable<NestedSetInsertionTracking.Identity> _source = null!;
    private HashSet<IUpdateEntry> _entries = null!;
    private HashSet<object> _entities = null!;
    private NestedSetCallbackWrites _writes = null!;

    /// <summary>Gets or sets the prepared identity producer representation.</summary>
    [Params("array", "list", "values", "projection", "flattened")]
    public string Shape { get; set; } = "array";

    /// <summary>Gets or sets the exact formerly measured completed-wave cardinality.</summary>
    [Params(0, 1, 64, 1_024)]
    public int Rows { get; set; }

    /// <summary>Builds actual native entries and source producers outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _owner = ManagedSaveContextOwner<CompletionContext>.Create(
            static options => new CompletionContext(options),
            false);
        var nodes = Enumerable.Range(1, Rows).Select(id => new CompletionNode { Id = id }).ToArray();
        _owner.Context.AttachRange(nodes);
        _identities = nodes.Select(node => NestedSetInsertionTracking.Capture(_owner.Context.Entry(node))).ToArray();
        _source = Shape switch
        {
            "array" => _identities,
            "list" => _identities.ToList(),
            "values" => _identities.Select((identity, index) => (identity, index))
                .ToDictionary(item => item.index, item => item.identity).Values,
            "projection" => _identities
                .Where(static identity => identity is not null)
                .Select(static identity => identity),
            "flattened" => new[] { _identities }.SelectMany(static batch => batch),
            _ => throw new ArgumentException("Unknown completed-identity producer shape.", nameof(Shape)),
        };

        _entries = _identities
            .Select(identity => identity.UpdateEntry)
            .ToHashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);
        _entities = nodes.ToHashSet<object>(ReferenceEqualityComparer.Instance);
        _writes = new NestedSetCallbackWrites();
    }

    /// <summary>Completes one existing wave without test-owned sampling or allocation accounting.</summary>
    [Benchmark]
    public void Complete() => _writes.RecordSavedRelationships(_source, _entries, _entities);

    /// <summary>Checks that completion preserves native identity count and has no database side effects.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (_identities.Any(identity => identity.HasCollectionRelationships)
                || _owner!.Context.ChangeTracker.Entries().Count() != Rows
                || _owner.Context.ChangeTracker.AutoDetectChangesEnabled
                || _owner.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
            {
                throw new InvalidOperationException("No-relationship completion changed its native input identities.");
            }
        }
        finally
        {
            _owner?.Dispose();
            _owner = null;
        }
    }

    /// <summary>Maps an exact native saved entry without relationships or store-generated keys.</summary>
    private sealed class CompletionContext : DbContext
    {
        /// <summary>Uses independently owned real SQLite services with detection disabled.</summary>
        internal CompletionContext(
            DbContextOptions<CompletionContext> options
        ) : base(options)
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<CompletionNode>().Property(node => node.Id).ValueGeneratedNever();
    }

    /// <summary>Supplies the one scalar key used by the completed native-identity fixture.</summary>
    private sealed class CompletionNode
    {
        /// <summary>Gets or sets the fixed native identity.</summary>
        public int Id { get; set; }
    }
}
