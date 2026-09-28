namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Shares one isolated model-compatibility database within this provider assembly.</summary>
[CollectionDefinition("Model compatibility")]
public sealed class
    ModelCompatibilityTestsDefinition : ICollectionFixture<
    ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine>>;

/// <summary>Prevents diagnostic listeners from contaminating this provider's allocation measurements.</summary>
// WHY: Collection definitions must live in the executable assembly to preserve measurement isolation.
[CollectionDefinition("Allocation measurements", DisableParallelization = true)]
public sealed class AllocationMeasurementIsolation;
