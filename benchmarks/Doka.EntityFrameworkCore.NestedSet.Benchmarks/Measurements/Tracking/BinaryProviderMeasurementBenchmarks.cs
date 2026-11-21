using System.Collections;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Observes complete binary provider equality and bounded structural hashing through all four routes.
/// </summary>
public class BinaryProviderMeasurementBenchmarks : IDisposable
{
    private BinaryContext _context = null!;
    private ComparerOperations _operations = null!;
    private object _first = null!;
    private object _second = null!;
    private byte[] _bytes = null!;

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

    /// <summary>Gets or sets native, erased, converted or converted-erased provider dispatch.</summary>
    [Params("native", "erased", "converted", "converted-erased")]
    public string Shape { get; set; } = "native";

    /// <summary>Gets or sets a binary length spanning the framework's structural hash window.</summary>
    [Params(0, 1, 4, 8, 9, 16, 32, 1024, 65_536)]
    public int Length { get; set; } = 32;

    /// <summary>Creates independent equal-content model inputs outside native measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _context = new BinaryContext();
        _operations = Operations(_context, Shape);
        _bytes = Enumerable.Range(0, Length).Select(index => (byte)(index % 251)).ToArray();
        _first = _operations.Model(_bytes, 10)!;
        _second = _operations.Model(_bytes.ToArray(), 20)!;
        VerifyWorkload();
    }

    /// <summary>Compares the complete independent provider values.</summary>
    /// <returns>Whether both binary representations are equal.</returns>
    [Benchmark]
    public bool Compare() => _operations.Compare(_first, _second);

    /// <summary>Hashes one actual model input using the production comparer.</summary>
    /// <returns>The framework-compatible bounded structural hash.</returns>
    [Benchmark]
    public int Hash() => _operations.Hash(_first);

    /// <summary>Checks semantic results without asserting a numeric measurement threshold.</summary>
    public void VerifyWorkload()
    {
        if (!Compare() || Hash() != StructuralComparisons.StructuralEqualityComparer.GetHashCode(_bytes))
        {
            throw new InvalidOperationException("The binary comparer workload changed.");
        }
    }

    /// <summary>Verifies the final result and releases only the invocation-owned model services.</summary>
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

    /// <summary>Builds delegates once so measurements include the real comparer but no new model wrappers.</summary>
    private static ComparerOperations Operations(
        BinaryContext context,
        string shape
    )
    {
        var metadata = context.Model.FindEntityType(typeof(BinaryNode))!;
        var converted = shape is "converted" or "converted-erased";
        var property = metadata.FindProperty(
            converted
                ? nameof(BinaryNode.Converted)
                : nameof(BinaryNode.Native))!;

        Func<byte[]?, int, object?> model = converted
            ? static (bytes, tag) => bytes is null
                ? null
                : new BinaryIdentity(bytes, tag)
            : static (bytes, _) => bytes;

        if (shape is "erased" or "converted-erased")
        {
            var comparer = new NestedSetProviderComparer<object>(property);

            return new ComparerOperations(comparer.Matches, comparer.GetHashCode, model);
        }

        if (converted)
        {
            var comparer = new NestedSetProviderComparer<BinaryIdentity>(property);

            return new ComparerOperations(
                (first, second) => comparer.Equals((BinaryIdentity?)first, (BinaryIdentity?)second),
                value => comparer.GetHashCode((BinaryIdentity)value),
                model);
        }

        var native = new NestedSetProviderComparer<byte[]>(property);

        return new ComparerOperations(
            (first, second) => native.Equals((byte[]?)first, (byte[]?)second),
            value => native.GetHashCode((byte[])value),
            model);
    }

    /// <summary>Retains prepared comparer and conversion delegates outside the measured loop.</summary>
    /// <param name="Compare">The actual equality entrypoint.</param>
    /// <param name="Hash">The actual provider hash entrypoint.</param>
    /// <param name="Model">The setup-only model-value factory.</param>
    private sealed record ComparerOperations(
        Func<object?, object?, bool> Compare,
        Func<object, int> Hash,
        Func<byte[]?, int, object?> Model
    );

    /// <summary>Provides native and converted binary property metadata without opening SQLite.</summary>
    private sealed class BinaryContext : TrackingMeasurementContext<BinaryContext>
    {
        /// <summary>Creates a model-only comparer context.</summary>
        internal BinaryContext() { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<BinaryNode>();
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            // WHY: Different tags retain different model identities but do not affect the provider bytes.
            node
                .Property(value => value.Converted)
                .HasConversion(value => value.Bytes, bytes => new BinaryIdentity(bytes, 0));
        }
    }

    /// <summary>Exposes independent native and converter-backed binary properties.</summary>
    private sealed class BinaryNode
    {
        /// <summary>Gets or sets the scalar model key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the native binary provider value.</summary>
        public byte[] Native { get; set; } = [];

        /// <summary>Gets or sets the reference identity normalized by its converter.</summary>
        public BinaryIdentity Converted { get; set; } = new([], 0);
    }

    /// <summary>Separates model equality from the binary provider representation.</summary>
    /// <param name="Bytes">The provider value, including the null-output fallback control.</param>
    /// <param name="Tag">Model-only identity data omitted by the configured converter.</param>
    private sealed record BinaryIdentity(
        byte[]? Bytes,
        int Tag
    );
}
