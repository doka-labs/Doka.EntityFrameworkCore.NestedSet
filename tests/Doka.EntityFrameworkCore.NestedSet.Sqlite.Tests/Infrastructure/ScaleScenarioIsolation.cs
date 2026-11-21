namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Prevents diagnostic listeners from contaminating this provider's functional scale and lifetime scenarios.</summary>

// WHY: Collection definitions must live in the executable assembly to preserve resource isolation.
[CollectionDefinition("Scale scenarios", DisableParallelization = true)]
public sealed class ScaleScenarioIsolation;
