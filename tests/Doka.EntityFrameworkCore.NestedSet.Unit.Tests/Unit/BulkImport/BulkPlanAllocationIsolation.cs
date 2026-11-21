namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Isolates process-wide heap measurements from other unit test collections.</summary>
[Trait("Category", "Allocation")]
[CollectionDefinition("BulkPlanAllocation", DisableParallelization = true)]
public sealed class BulkPlanAllocationIsolation;
