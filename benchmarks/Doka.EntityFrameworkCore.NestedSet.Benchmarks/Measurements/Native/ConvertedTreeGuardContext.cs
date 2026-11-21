namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Isolates one physical key mapping and its EF model-cache identity from the other guard profiles.</summary>
internal sealed class ConvertedTreeGuardContext : NativeGuardContext
{
    /// <summary>Uses the provider and catalog owned by the current benchmark iteration.</summary>
    internal ConvertedTreeGuardContext(DbContextOptions options) : base(options) { }
}
