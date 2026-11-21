namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Isolates one physical key mapping and its EF model-cache identity from the other guard profiles.</summary>
internal sealed class ConvertedIntegerGuardContext : NativeGuardContext
{
    /// <summary>Uses the provider and catalog owned by the current benchmark iteration.</summary>
    internal ConvertedIntegerGuardContext(DbContextOptions options) : base(options) { }
}
