namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Prevents diagnostic listeners from contaminating this provider's functional scale and lifetime scenarios.</summary>

// WHY: Collection definitions must live in the executable assembly to preserve resource isolation.
// WHY: xUnit v3 propagates collection traits to each test; CI runs this serial collection on its own runners.
[Trait("Lane", "Scale")]
[CollectionDefinition("Scale scenarios", DisableParallelization = true)]
public sealed class ScaleScenarioIsolation;
