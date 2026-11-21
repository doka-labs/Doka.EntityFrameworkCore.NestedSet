namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies one anchor's exact scalar contribution without carrying an entity instance.</summary>
/// <param name="Anchor">The application-selected anchor key.</param>
/// <param name="Contributions">The number of payload rows beneath that anchor.</param>
/// <param name="KeySum">The sum of contributed payload keys.</param>
internal sealed record CompositionCapacityTotal(
    int Anchor,
    long Contributions,
    long KeySum
);
