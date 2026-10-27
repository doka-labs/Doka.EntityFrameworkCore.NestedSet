namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Isolates allocation measurements from concurrent diagnostic observers in the Unit assembly.</summary>
// WHY: xUnit discovers collection definitions only in the executable assembly, not referenced libraries.
// WHY: xUnit v3 propagates collection traits to each test; CI/RC measure these cases without instrumentation.
[Trait("Category", "Allocation")]
[CollectionDefinition("Allocation measurements", DisableParallelization = true)]
public sealed class AllocationMeasurementIsolation;
