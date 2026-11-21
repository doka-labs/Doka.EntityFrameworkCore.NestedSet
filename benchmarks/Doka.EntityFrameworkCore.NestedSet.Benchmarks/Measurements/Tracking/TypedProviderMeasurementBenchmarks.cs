namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes typed and erased scalar comparison at actual native and configured converter boundaries.</summary>
public class TypedProviderMeasurementBenchmarks : IDisposable
{
    private ComparerContext _context = null!;
    private Func<bool> _typed = null!;
    private Func<bool> _erased = null!;

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

    /// <summary>Gets or sets the native integer, Guid, nullable parent or converted-value representation.</summary>
    [Params("Int", "Guid", "NullableParent", "Converted")]
    public string Role { get; set; } = "Int";

    /// <summary>Prepares real metadata and both independent comparison entrypoints.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _context = new ComparerContext();
        var metadata = _context.Model.FindEntityType(typeof(ComparerNode))!;
        if (Role == "Guid")
        {
            var comparer = new NestedSetProviderComparer<Guid>(metadata.FindProperty(nameof(ComparerNode.TreeId))!);
            var id = Guid.Parse("74100000-0000-0000-0000-000000000001");
            _typed = () => comparer.Equals(id, id) && !comparer.Equals(id, Guid.Empty);
            _erased = () => comparer.Matches(id, id) && !comparer.Matches(id, Guid.Empty);
        }
        else if (Role == "Converted")
        {
            var comparer = new NestedSetProviderComparer<ConvertedIdentity>(
                metadata.FindProperty(nameof(ComparerNode.ConvertedId))!);
            var first = new ConvertedIdentity(17);
            var equal = new ConvertedIdentity(27);
            var different = new ConvertedIdentity(18);
            _typed = () => comparer.Equals(first, equal) && !comparer.Equals(first, different);
            _erased = () => comparer.Matches(first, equal) && !comparer.Matches(first, different);
        }
        else
        {
            var name = Role == "NullableParent" ? nameof(ComparerNode.ParentId) : nameof(ComparerNode.Id);
            var comparer = new NestedSetProviderComparer<int>(metadata.FindProperty(name)!);
            _typed = () => comparer.Equals(0, 0) && !comparer.Equals(0, 1);
            _erased = () => comparer.Matches(0, 0) && !comparer.Matches(0, 1);
        }

        VerifyWorkload();
    }

    /// <summary>Compares equal and unequal inputs through typed production dispatch.</summary>
    /// <returns>Whether both positive and negative controls retain their meaning.</returns>
    [Benchmark]
    public bool TypedComparison() => _typed();

    /// <summary>Compares the same controls through metadata-erased production dispatch.</summary>
    /// <returns>Whether both comparison controls remain correct.</returns>
    [Benchmark(Baseline = true)]
    public bool ErasedComparison() => _erased();

    /// <summary>Rejects a changed fixture or converter instead of treating it as a performance result.</summary>
    public void VerifyWorkload()
    {
        if (!TypedComparison() || !ErasedComparison())
        {
            throw new InvalidOperationException("The typed provider comparison workload changed.");
        }
    }

    /// <summary>Releases the actual provider services after final semantic verification.</summary>
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

    /// <summary>Supplies actual native and nullable relational property metadata without opening a database.</summary>
    private sealed class ComparerContext : TrackingMeasurementContext<ComparerContext>
    {
        /// <summary>Creates a metadata-only context for the real provider comparer.</summary>
        internal ComparerContext() { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<ComparerNode>();
            node
                .Property(entity => entity.Id)
                .ValueGeneratedNever();

            // WHY: Distinct model values intentionally share a normalized provider representation, so skipping
            // the actual configured converter would invalidate both equality controls and the observed BDN workload.
            node
                .Property(entity => entity.ConvertedId)
                .HasConversion(value => value.Value % 10, value => new ConvertedIdentity(value));
        }
    }

    /// <summary>Separates the native scalar key, nullable parent storage and non-null Guid identity.</summary>
    private sealed class ComparerNode
    {
        /// <summary>Gets or sets the assigned native integer key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the nullable integer parent.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the native Guid identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the converted scalar whose provider representation is normalized.</summary>
        public ConvertedIdentity ConvertedId { get; set; }
    }

    /// <summary>Separates model equality from the explicitly configured normalized integer representation.</summary>
    /// <param name="Value">The original model identity.</param>
    private readonly record struct ConvertedIdentity(
        int Value
    );
}
