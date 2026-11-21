using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class IndependentAncestorEnvelopeTestBase
{
    /// <summary>Independent consumer occurrences remain a bag after structural owner matching.</summary>
    [Fact]
    public async Task SelectAncestors_RepeatedConsumerRows_RetainEveryAncestorOccurrence()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(
            _fixture,
            Engine,
            scope,
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            hidden: null,
            closed: null,
            planProbe: null,
            chainNodes: null,
            ownerMode: CompositionTestData.ChainOwnerMode.RootValue);

        await context.AddRangeAsync(
            [
                new CompositionInputOne
                {
                    Scope = scope,
                    NodeId = 3,
                    Date = new DateOnly(2044, 10, 1),
                    Amount = 2,
                },
                new CompositionInputOne
                {
                    Scope = scope,
                    NodeId = 3,
                    Date = new DateOnly(2044, 10, 2),
                    Amount = 3,
                },
            ],
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var pairs = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id == 3,
                (source, owner, distance) => new
                {
                    Source = source,
                    Owner = owner,
                });

        var query = pairs
            .Join(
                context.Set<CompositionInputOne>(),
                pair => new
                {
                    pair.Source.Scope,
                    NodeId = pair.Source.Id,
                },
                fact => new
                {
                    fact.Scope,
                    fact.NodeId,
                },
                (pair, fact) => new
                {
                    Owner = pair.Owner.Id,
                    fact.Amount,
                })
            .OrderBy(row => row.Owner)
            .ThenBy(row => row.Amount);

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 1, 2, 2], rows.Select(row => row.Owner));
        Assert.Equal([2, 3, 2, 3], rows.Select(row => row.Amount));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An independent upward source can still feed a correlated downward pivot and both edge proofs.</summary>
    [Fact]
    public async Task SelectAncestors_MixedDirections_RetainOriginalSourcePivotAndTarget()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(
            _fixture,
            Engine,
            scope,
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            hidden: null,
            closed: null,
            planProbe: null,
            chainNodes: null,
            ownerMode: CompositionTestData.ChainOwnerMode.RootValue);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(scope);

        var up = hierarchy
            .SelectAncestors(
                node => node.Id == 3 || node.Id == 4,
                (source, pivot, distance) => new
                {
                    Source = source,
                    Pivot = pivot,
                    Distance = distance,
                })
            .Where(row => row.Distance == 1);

        var down = hierarchy
            .SelectSubtrees(
                pivot => up.Any(row => row.Pivot == pivot),
                (pivot, target, distance) => new
                {
                    Pivot = pivot,
                    Target = target,
                })
            .Where(row => row.Target.Id == 4);

        var mixed = up.Join(
            down,
            row => row.Pivot,
            row => row.Pivot,
            (first, second) => new
            {
                first.Source,
                first.Pivot,
                second.Target,
            });

        var allowedUp = hierarchy.WherePathEdges(
            mixed,
            row => row.Pivot,
            row => row.Source,
            (_, parent, child) => child.EdgeOpen);

        var query = hierarchy
            .WherePathEdges(
                allowedUp,
                row => row.Pivot,
                row => row.Target,
                (_, parent, child) => child.EdgeOpen)
            .OrderBy(row => row.Source.Id)
            .Select(row => new
            {
                Source = row.Source.Id,
                Pivot = row.Pivot.Id,
                Target = row.Target.Id,
            });

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3, 4], rows.Select(row => row.Source));
        Assert.Equal([2, 2], rows.Select(row => row.Pivot));
        Assert.Equal([4, 4], rows.Select(row => row.Target));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Duplicate independent facts select a node once while downstream consumer occurrences remain a bag.
    /// </summary>
    /// <param name="shape">An embedded fact root, ordinary captured query, or hierarchy-dependent fact query.</param>
    [Theory]
    [InlineData("embedded")]
    [InlineData("captured")]
    [InlineData("hierarchy")]
    public async Task SelectAncestors_FactSelection_PreservesSourceIdentityAndConsumerOccurrences(
        string shape
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await CompositionTestData.CreateContextAsync(
            _fixture,
            Engine,
            scope,
            probe,
            planProbe: capture,
            cancellationToken: TestContext.Current.CancellationToken,
            hidden: null,
            closed: null,
            chainNodes: null,
            ownerMode: CompositionTestData.ChainOwnerMode.RootValue);

        await context.AddRangeAsync(
            [
                new CompositionInputOne
                {
                    Scope = scope,
                    NodeId = 3,
                    Date = new DateOnly(2044, 10, 1),
                    Amount = 2,
                },
                new CompositionInputOne
                {
                    Scope = scope,
                    NodeId = 3,
                    Date = new DateOnly(2044, 10, 2),
                    Amount = 3,
                },
            ],
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var facts = context
            .Set<CompositionInputOne>()
            .Where(fact => fact.Scope == scope);

        var selection = shape switch
        {
            "embedded" => FactSelection(
                facts,
                (node, fact) => fact.Scope == node.Scope && fact.NodeId == node.Id),
            "captured" => node => facts.Any(fact => fact.Scope == node.Scope && fact.NodeId == node.Id),
            "hierarchy" => node => facts.Any(fact => fact.Scope == node.Scope
                && fact.NodeId == node.Id
                && context
                    .Set<CompositionNode>()
                    .Any(hidden => hidden.Scope == fact.Scope && hidden.Id == fact.NodeId)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var pairs = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                selection,
                (source, owner, distance) => new
                {
                    Source = source,
                    Owner = owner,
                });

        var query = pairs
            .Join(
                facts,
                pair => pair.Source.Id,
                fact => fact.NodeId,
                (pair, fact) => new
                {
                    Source = pair.Source.Id,
                    Owner = pair.Owner.Id,
                    fact.Amount,
                })
            .OrderBy(row => row.Owner)
            .ThenBy(row => row.Amount);

        var enabled = shape != "hierarchy"
            && NestedSetProviderCapabilities.Resolve(context)
                .CompletedSourceMethod is not null
            && NestedSetProviderCapabilities.Resolve(context)
                .OrderedJoinMethod is not null;

        // WHY: Seed and consumer writes precede the one-reader contract measured below.
        capture.Commands.Clear();
        capture.ParameterValues.Clear();
        capture.ParameterNames.Clear();
        capture.ParameterBindings.Clear();
        probe.Reset();

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(shape != "hierarchy", enabled);
        Assert.Equal([3, 3, 3, 3], rows.Select(row => row.Source));
        Assert.Equal([1, 1, 2, 2], rows.Select(row => row.Owner));
        Assert.Equal([2, 3, 2, 3], rows.Select(row => row.Amount));
        var sql = capture.Commands.Single();
        Assert.Equal(enabled, sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));

        if (enabled)
        {
            var completedSource = sql[..sql.IndexOf("UNION ALL", StringComparison.OrdinalIgnoreCase)];
            Assert.Contains("SELECT DISTINCT", completedSource, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Uses an actual independent EF root to exercise the noncaptured normalized source contract.</summary>
    private static Expression<Func<CompositionNode, bool>> FactSelection(
        IQueryable<CompositionInputOne> facts,
        Expression<Func<CompositionNode, CompositionInputOne, bool>> match
    )
    {
        var exists = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Any),
            [typeof(CompositionInputOne)],
            facts.Expression,
            Expression.Quote(Expression.Lambda<Func<CompositionInputOne, bool>>(match.Body, match.Parameters[1])));

        return Expression.Lambda<Func<CompositionNode, bool>>(exists, match.Parameters[0]);
    }
}
