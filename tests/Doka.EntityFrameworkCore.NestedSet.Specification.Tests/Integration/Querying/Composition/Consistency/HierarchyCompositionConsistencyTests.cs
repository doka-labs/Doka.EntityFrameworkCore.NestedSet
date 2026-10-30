namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies asynchronous terminals and real transactional snapshots of composed hierarchy queries.</summary>
[Collection("Model compatibility")]
public abstract partial class HierarchyCompositionConsistencyTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Reuses the provider-owned database and the neutral composition mapping.</summary>
    /// <param name="fixture">The collection-owned fixture for the current engine.</param>
    protected HierarchyCompositionConsistencyTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Seeds the shared small tree and one independent payload row in a test-private scope.</summary>
    private async Task<CompositionContext> CreateContextAsync(
        Guid scope,
        CompositionCommandProbe probe
    )
    {
        var context = await CompositionTestData.CreateContextAsync(_fixture, Engine, scope, probe);

        try
        {
            await context.AddAsync(
                new CompositionInputOne
                {
                    Scope = scope,
                    NodeId = 3,
                    Date = new DateOnly(2044, 10, 1),
                    Amount = 10,
                },
                CancellationToken.None);

            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Combines downward and upward pairs, both owner filters, a temporal path and separate payload.</summary>
    private static IQueryable<CompositionSnapshotRow> BuildMixedQuery(
        CompositionContext context,
        Guid scope
    )
    {
        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(scope);

        var asOf = new DateOnly(2044, 10, 15);
        var down = hierarchy.SelectSubtrees(
            anchor => anchor.Id == 1,
            (anchor, node, distance) => new
            {
                Anchor = anchor,
                Node = node,
                DownDistance = distance,
            });

        var up = hierarchy.SelectAncestorsOrSelf(
            node => node.Id == 3,
            (node, upper, distance) => new
            {
                Node = node,
                Upper = upper,
                UpDistance = distance,
            });

        var pairs = down.Join(
            up,
            row => row.Node,
            row => row.Node,
            (first, second) => new
            {
                first.Anchor,
                first.Node,
                second.Upper,
                first.DownDistance,
                second.UpDistance,
            });

        var open = hierarchy.WherePathEdges(
            pairs,
            row => row.Anchor,
            row => row.Node,
            (row, parent, child) => child.EdgeOpen && child.FromDate <= asOf && (child.ToDate == null || child.ToDate >= asOf));

        var any = hierarchy.WhereAnyAncestorOrSelf(
            open,
            row => row.Node,
            (row, owner) => owner.Matches);

        var nearest = hierarchy.WhereNearestAncestorOrSelf(
            any,
            row => row.Node,
            (row, owner) => owner.Id == 1,
            (row, owner) => owner.Matches);

        return nearest
            .Join(
                context
                    .Set<CompositionInputOne>()
                    .Where(fact => fact.Scope == scope),
                row => row.Node.Id,
                fact => fact.NodeId,
                (row, fact) => new CompositionSnapshotRow
                {
                    Upper = row.Upper.Id,
                    Parent = row.Node.ParentId,
                    DownDistance = row.DownDistance,
                    UpDistance = row.UpDistance,
                    Matches = row.Node.Matches,
                    FromDate = row.Node.FromDate,
                    ToDate = row.Node.ToDate,
                    Amount = fact.Amount,
                })
            .OrderBy(row => row.Upper);
    }
}
