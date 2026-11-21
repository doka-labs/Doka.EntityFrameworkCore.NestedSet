namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Retains only the registry values that an atomic operation can mutate.</summary>
internal sealed record CapacityRegistryState(
    long Revision,
    byte Lifecycle
);
