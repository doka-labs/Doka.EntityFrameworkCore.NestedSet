namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Observes insertion identity capture, refresh, comparison and detach preparation across actual EF storage shapes.
/// </summary>
public class InsertionIdentityMeasurementBenchmarks : IDisposable
{
    private AllocationContext _context = null!;
    private NestedSetInsertionTracking.Identity _identity = null!;
    private IReadOnlyList<IProperty> _properties = null!;

    /// <summary>Releases owned fixture resources independently of semantic verification.</summary>
    public void Dispose()
    {
        try
        {
            Dispose(disposing: true);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Releases managed fixture resources once, including failed or incomplete setup.</summary>
    /// <param name="disposing">Whether this call owns managed-resource cleanup.</param>
    protected virtual void Dispose(
        bool disposing
    )
    {
        if (!disposing)
        {
            return;
        }

        var context = _context;
        _context = null!;
        context?.Dispose();
    }

    /// <summary>Gets or sets the assigned, generated, shadow, field, compound or indexer representation.</summary>
    [Params("int", "guid", "compound", "generated", "shadow", "string", "field", "indexer", "indexer-sentinel")]
    public string Shape { get; set; } = "int";

    /// <summary>Creates and validates the exact tracked identity outside measured invocations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _context = new AllocationContext();
        var entry = Shape == "indexer-sentinel"
            ? _context.Set<Dictionary<string, object>>("IndexerNode").Add(new Dictionary<string, object> { ["Id"] = 0 })
            : CreateEntry(_context, Shape);
        _identity = NestedSetInsertionTracking.Capture(entry);
        _properties = entry.Metadata.FindPrimaryKey()!.Properties;
        if (Shape == "indexer-sentinel")
        {
            ((Dictionary<string, object>)entry.Entity)["Id"] = 37;
        }

        VerifyWorkload();
    }

    /// <summary>Refreshes the existing tracked identity through the production storage reader.</summary>
    [Benchmark]
    public void Refresh() => _identity.Refresh();

    /// <summary>Compares every captured key component without adding reflection or wrappers.</summary>
    /// <returns>Whether every actual key component still matches.</returns>
    [Benchmark]
    public bool Matches()
    {
        var matches = true;

        // WHY: Composite identities require visiting every component without a short-circuit or iterator wrapper.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _properties.Count; index++)
        {
            matches &= _identity.Matches(_properties[index]);
        }

        return matches;
    }

    /// <summary>Prepares the same existing identity for detachment.</summary>
    [Benchmark]
    public void PrepareDetach() => _identity.PrepareDetach();

    /// <summary>Requires actual native/CLR identity semantics without an allocation acceptance threshold.</summary>
    public void VerifyWorkload()
    {
        Refresh();
        if (!Matches())
        {
            throw new InvalidOperationException("The insertion identity workload no longer matches its captured keys.");
        }
    }

    /// <summary>Verifies the completed workload and releases its owned provider.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            VerifyWorkload();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>Creates a tracked identity with its exact scalar, compound, shadow or generated storage.</summary>
    private static EntityEntry CreateEntry(
        AllocationContext context,
        string shape
    )
    {
        if (shape == "shadow")
        {
            var entry = context.Entry(new ShadowNode());
            entry.Property("Id")
                .CurrentValue = 1;

            entry.State = EntityState.Added;

            return entry;
        }

        if (shape == "indexer")
        {
            return context
                .Set<Dictionary<string, object>>("IndexerNode")
                .Add(new Dictionary<string, object> { ["Id"] = 1 });
        }

        var entity = shape switch
        {
            "int" => (object)new IntNode { Id = 1 },
            "guid" => new GuidNode { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") },
            "compound" => new CompoundNode
            {
                Scope = 1,
                Id = 2,
            },
            "generated" => new GeneratedNode(),
            "string" => new StringNode { Id = "one" },
            "field" => new FieldNode(1),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        return context.Add(entity);
    }

    /// <summary>Requires the null protection supplied by EF's object comparer wrapper.</summary>
    private static bool NonNullTextEquals(
        string? left,
        string? right
    )
    {
        // WHY: Extracting a typed comparer body must not bypass EF's operand null guards.
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return string.Equals(left, right, StringComparison.Ordinal);
    }

    /// <summary>Finalizes the production EF identity shapes without database I/O.</summary>
    internal sealed class AllocationContext : TrackingMeasurementContext<AllocationContext>
    {
        /// <summary>Supplies relational metadata without opening a connection.</summary>
        protected override void OnConfiguring(
            DbContextOptionsBuilder options
        ) => options.UseSqlite("Data Source=:memory:");

        /// <summary>Configures the scalar, sidecar, relationship, and property-bag comparison controls.</summary>
        protected override void OnModelCreating(
            ModelBuilder model
        )
        {
            model
                .Entity<IntNode>()
                .HasKey(node => node.Id);

            model
                .Entity<IntNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            model
                .Entity<GuidNode>()
                .HasKey(node => node.Id);

            model
                .Entity<GuidNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            model
                .Entity<CompoundNode>()
                .HasKey(node => new
                {
                    node.Scope,
                    node.Id,
                });

            model
                .Entity<CompoundNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            model
                .Entity<GeneratedNode>()
                .HasKey(node => node.Id);

            model
                .Entity<GeneratedNode>()
                .Property(node => node.Id)
                .ValueGeneratedOnAdd();

            model
                .Entity<ShadowNode>()
                .Property<int>("Id")
                .ValueGeneratedNever();

            model
                .Entity<ShadowNode>()
                .HasKey("Id");

            model
                .Entity<StringNode>()
                .HasKey(node => node.Id);

            var text = model
                .Entity<StringNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            text.Metadata.SetValueComparer(
                new ValueComparer<string>(
                    (left, right) => NonNullTextEquals(left, right),
                    value => value.GetHashCode(),
                    value => value));

            model
                .Entity<FieldNode>()
                .HasKey(node => node.Id);

            model
                .Entity<FieldNode>()
                .Property(node => node.Id)
                .HasField("_id")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .ValueGeneratedNever();

            model.SharedTypeEntity<Dictionary<string, object>>(
                "IndexerNode",
                bag =>
                {
                    bag
                        .IndexerProperty<int>("Id")
                        .HasSentinel(37)
                        .ValueGeneratedNever();

                    bag.HasKey("Id");
                });

            model
                .Entity<ForeignNode>()
                .HasKey(node => node.Id);

            model
                .Entity<ForeignNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            model
                .Entity<ForeignNode>()
                .HasOne<ForeignNode>()
                .WithMany()
                .HasForeignKey(node => node.ParentId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        }
    }

    internal sealed class IntNode
    {
        /// <summary>Gets or sets the assigned scalar identity.</summary>
        public int Id { get; set; }
    }

    private sealed class GuidNode
    {
        /// <summary>Gets or sets the assigned Guid identity.</summary>
        public Guid Id { get; set; }
    }

    private sealed class CompoundNode
    {
        /// <summary>Gets or sets the first composite key component.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the second composite key component.</summary>
        public int Id { get; set; }
    }

    private sealed class GeneratedNode
    {
        /// <summary>Gets or sets the provider-generated scalar identity.</summary>
        public int Id { get; set; }
    }

    private sealed class ShadowNode;

    private sealed class StringNode
    {
        /// <summary>Gets or sets the immutable reference identity.</summary>
        public string? Id { get; set; } = string.Empty;
    }

    private sealed class FieldNode
    {
        /// <summary>Initializes the benchmark fixture with its explicit dependencies.</summary>
        public FieldNode(
            int id
        )
        {
            _id = id;
        }

        private readonly int _id;

        /// <summary>Gets the identity read through its configured backing field.</summary>
        // WHY: EF explicitly binds _id; an auto-property would invalidate the field-access fixture.
        // ReSharper disable once ConvertToAutoProperty
        public int Id => _id;
    }

    private sealed class ForeignNode
    {
        /// <summary>Gets or sets the assigned identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the required FK whose tracker value can be conceptually null.</summary>
        public int ParentId { get; set; }
    }
}
