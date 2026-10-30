using ChainOwnerMode = Doka.EntityFrameworkCore.NestedSet.Tests.CompositionTestData.ChainOwnerMode;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies deferred generic hierarchy composition through actual provider SQL.</summary>
[Collection("Model compatibility")]
public abstract partial class HierarchyCompositionTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the current provider's database without duplicating the shared query contracts.</summary>
    /// <param name="fixture">The collection-owned model database for this engine.</param>
    protected HierarchyCompositionTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Creates a neutral hierarchy using the shared behavior-preserving seed.</summary>
    private Task<CompositionContext> CreateContextAsync(
        Guid scope,
        CompositionCommandProbe probe,
        int? hidden = null,
        int? closed = null,
        EnterpriseProbe? planProbe = null,
        int? chainNodes = null,
        ChainOwnerMode ownerMode = ChainOwnerMode.RootValue
    ) => CompositionTestData.CreateContextAsync(
        _fixture, Engine, scope, probe, hidden, closed, planProbe, chainNodes, ownerMode);

    /// <summary>Persists three independent fact tables with identical measurement identities.</summary>
    private static async Task SeedReadingsAsync(
        CompositionContext context,
        Guid scope
    )
    {
        (int NodeId, DateOnly Date, int Amount)[] readings =
        [
            (3, new DateOnly(2044, 10, 1), 10),
            (4, new DateOnly(2044, 10, 1), 20),
            (6, new DateOnly(2044, 10, 1), 30),
            (3, new DateOnly(2044, 11, 1), 40),
            (6, new DateOnly(2044, 11, 1), 50),
        ];

        foreach (var (nodeId, date, amount) in readings)
        {
            await context.AddRangeAsync(
            [
                new CompositionInputOne { Scope = scope, NodeId = nodeId, Date = date, Amount = amount },
                new CompositionInputTwo { Scope = scope, NodeId = nodeId, Date = date, Amount = amount * 10 },
                new CompositionInputThree { Scope = scope, NodeId = nodeId, Date = date, Amount = amount + 5 },
            ], CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }
}
