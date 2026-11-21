namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Isolates actual GC-lifetime controls from instrumentation and unrelated lifetime observers.</summary>

// WHY: xUnit discovers collection definitions only in the executable assembly, not referenced libraries.
// WHY: The selected coverage collector retains collectible modules; these functional controls run without it.
[Trait("Category", "CollectorSensitive")]
[CollectionDefinition("Collector-sensitive lifetimes", DisableParallelization = true)]
public sealed class CollectorSensitiveLifetimeIsolation;
