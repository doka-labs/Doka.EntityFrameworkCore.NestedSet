namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Isolates the generated keys and tree identities of one deletion case.</summary>
internal readonly record struct MappingDeleteScenario(
    int Seed,
    Guid TargetTree,
    Guid OtherTree
);
