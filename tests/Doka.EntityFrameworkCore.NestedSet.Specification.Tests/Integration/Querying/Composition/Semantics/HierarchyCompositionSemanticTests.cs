namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies occurrence, endpoint, visibility, and consumer-independent hierarchy semantics.</summary>
[Collection("Model compatibility")]
public abstract partial class HierarchyCompositionSemanticTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the current engine's shared model database for independent small hierarchy scopes.</summary>
    /// <param name="fixture">The provider-owned database fixture.</param>
    protected HierarchyCompositionSemanticTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Creates the shared six-node payload without changing another suite's seed semantics.</summary>
    private Task<CompositionContext> CreateContextAsync(
        Guid scope,
        CompositionCommandProbe probe,
        int? hidden = null,
        EnterpriseProbe? sql = null,
        int? chainNodes = null
    ) => CompositionTestData.CreateContextAsync(
        _fixture, Engine, scope, probe, hidden: hidden, planProbe: sql, chainNodes: chainNodes);

    /// <summary>Evaluates a path independently of the nested-set bounds used by the production query.</summary>
    private static IEnumerable<int> Ancestors(
        IReadOnlyDictionary<int, int?> parents,
        int node,
        bool includeSelf
    )
    {
        if (includeSelf)
        {
            yield return node;
        }

        while (parents[node] is { } parent)
        {
            yield return parent;
            node = parent;
        }
    }

    /// <summary>Computes structural distance from the independent direct-parent list.</summary>
    private static int Distance(
        IReadOnlyDictionary<int, int?> parents,
        int upper,
        int lower
    ) => Ancestors(parents, lower, includeSelf: true).TakeWhile(node => node != upper).Count();
}
