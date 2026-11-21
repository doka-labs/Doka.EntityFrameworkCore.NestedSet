using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Retains seeded identities solely for independent result and cursor assertions outside timing.</summary>
/// <param name="Tenant">The authorized isolated tenant.</param>
/// <param name="Request">The explicit finite chain and safety bounds.</param>
/// <param name="People">The source, then each level's measured managers.</param>
/// <param name="Groups">The nine management levels followed by the measured filler branch.</param>
internal sealed record ChainBenchmarkFixture(
    Guid Tenant,
    SupervisorChainRequest Request,
    int[] People,
    int[] Groups
);
