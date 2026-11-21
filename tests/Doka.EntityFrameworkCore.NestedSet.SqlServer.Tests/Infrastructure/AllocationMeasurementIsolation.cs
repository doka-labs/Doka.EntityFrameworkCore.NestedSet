namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Prevents diagnostic listeners from contaminating this provider's allocation measurements.</summary>

// WHY: Collection definitions must live in the executable assembly to preserve measurement isolation.
[CollectionDefinition("Allocation measurements", DisableParallelization = true)]
public sealed class AllocationMeasurementIsolation;
