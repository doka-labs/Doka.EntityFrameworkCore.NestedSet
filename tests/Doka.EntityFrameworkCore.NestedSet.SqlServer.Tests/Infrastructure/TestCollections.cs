namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Shares isolated model-compatibility resources within this provider collection.</summary>
[CollectionDefinition("Model compatibility")]
public sealed class ModelCompatibilityTestsDefinition :
    ICollectionFixture<ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine>>;

/// <summary>Prevents diagnostic listeners from contaminating this provider's allocation measurements.</summary>
// WHY: Collection definitions must live in the executable assembly to preserve measurement isolation.
[CollectionDefinition("Allocation measurements", DisableParallelization = true)]
public sealed class AllocationMeasurementIsolation;
