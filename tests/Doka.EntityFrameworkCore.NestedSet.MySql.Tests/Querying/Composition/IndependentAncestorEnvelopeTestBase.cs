using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Preserves unindexed ancestor semantics when a completed independent source narrows candidates.</summary>
public abstract class IndependentAncestorEnvelopeTestBase : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the concrete engine's existing collection-owned model database.</summary>
    /// <param name="fixture">The exact provider fixture.</param>
    protected IndependentAncestorEnvelopeTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>An empty completed source produces no envelope and no synthetic owner.</summary>
    [Fact]
    public async Task EmptySelectionReturnsNoAncestors()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(_fixture, Engine, scope, probe);
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id < 0,
                (source, owner, distance) => owner.Id);

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(rows);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Identical intervals in different trees cannot cross-match their sources or ancestors.</summary>
    [Fact]
    public async Task EqualBoundsInDifferentTreesRetainExactAncestry()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(_fixture, Engine, scope, probe);
        var secondTree = Guid.NewGuid();
        await context.AddRangeAsync(
            [
                Node(scope, secondTree, 101, null, 1, 12, 0),
                Node(scope, secondTree, 102, 101, 2, 7, 1),
                Node(scope, secondTree, 103, 102, 3, 4, 2),
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id == 3 || node.Id == 103,
                (source, owner, distance) => new
                {
                    Source = source.Id,
                    Owner = owner.Id,
                    Distance = distance,
                });

        // Act
        var rows = await query
            .OrderBy(row => row.Source)
            .ThenBy(row => row.Owner)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3, 3, 103, 103], rows.Select(row => row.Source));
        Assert.Equal([1, 2, 101, 102], rows.Select(row => row.Owner));
        Assert.Equal([2, 1, 2, 1], rows.Select(row => row.Distance));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Unbound candidate access cannot introduce another scope with identical keys, tree and intervals.</summary>
    [Fact]
    public async Task EqualTreeKeysAndBoundsInAnotherScopeDoNotDuplicateOwners()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var foreignScope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(_fixture, Engine, scope, probe);
        var tree = await context
            .Set<CompositionNode>()
            .Where(node => node.Scope == scope)
            .Select(node => node.TreeId)
            .FirstAsync(CancellationToken.None);

        await context.AddRangeAsync(
            [
                Node(foreignScope, tree, 1, null, 1, 12, 0),
                Node(foreignScope, tree, 2, 1, 2, 7, 1),
                Node(foreignScope, tree, 3, 2, 3, 4, 2),
            ],
            CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id == 3,
                (source, owner, distance) => new
                {
                    Source = source.Scope,
                    Owner = owner.Id,
                    OwnerScope = owner.Scope,
                })
            .OrderBy(row => row.Owner);

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2], rows.Select(row => row.Owner));
        Assert.All(rows, row => Assert.Equal(scope, row.Source));
        Assert.All(rows, row => Assert.Equal(scope, row.OwnerScope));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A hidden mapped owner remains invisible after coordinate filtering and payload hydration.</summary>
    [Fact]
    public async Task HiddenAncestorIsNotReintroducedByTheEnvelope()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context =
            await CompositionTestData.CreateContextAsync(_fixture, Engine, scope, probe, hidden: 2);
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id == 3,
                (source, owner, distance) => owner.Id);

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], rows);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Independent consumer occurrences remain a bag after structural owner matching.</summary>
    [Fact]
    public async Task RepeatedConsumerRowsRetainEveryAncestorOccurrence()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(_fixture, Engine, scope, probe);
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
    public async Task MixedDirectionsRetainOriginalSourcePivotAndTarget()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(_fixture, Engine, scope, probe);
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

    /// <summary>The statement contains one server-volatile source selection and preserves its actual complete result.</summary>
    [Fact]
    public async Task VolatileSelectionIsNotCopiedIntoTheEnvelope()
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
            planProbe: capture);

        // WHY: RAND is server-volatile, but its documented nonnegative range gives this test a fixed
        // ancestry oracle. Its single SQL occurrence guards source sharing independently of that oracle.
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => EF.Functions.Random() >= 0,
                (source, owner, distance) => new
                {
                    Source = source.Id,
                    Owner = owner.Id,
                })
            .OrderBy(row => row.Source)
            .ThenBy(row => row.Owner);

        // WHY: This observer also captured the fixture seed. Retain only commands and matching bindings
        // from the actual query boundary so Single guards the reader contract rather than setup work.
        capture.Commands.Clear();
        capture.ParameterValues.Clear();
        capture.ParameterNames.Clear();
        capture.ParameterBindings.Clear();
        probe.Reset();

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([2, 3, 3, 4, 4, 5, 6, 6], rows.Select(row => row.Source));
        Assert.Equal([1, 1, 2, 1, 2, 1, 1, 5], rows.Select(row => row.Owner));
        Assert.Equal(
            1,
            capture
                .Commands
                .Single()
                .Split("RAND()", StringSplitOptions.None)
                .Length
            - 1);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Duplicate independent facts select a node once while downstream consumer occurrences remain a bag.</summary>
    /// <param name="shape">An embedded fact root, ordinary captured query, or hierarchy-dependent fact query.</param>
    [Theory]
    [InlineData("embedded")]
    [InlineData("captured")]
    [InlineData("hierarchy")]
    public async Task FactSelectionPreservesSourceIdentityAndConsumerOccurrences(
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
            planProbe: capture);

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

    /// <summary>Necessary source limits use no sentinel arithmetic near the maximum supported bound.</summary>
    [Fact]
    public async Task ExtremeBoundsRetainTheStrictAncestor()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options),
            probe);

        await context.AddRangeAsync(
            [
                Node(scope, tree, 1, null, 1, long.MaxValue, 0),
                Node(scope, tree, 2, 1, long.MaxValue - 2, long.MaxValue - 1, 1),
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id == 2,
                (source, owner, distance) => new
                {
                    Source = source.Id,
                    Owner = owner.Id,
                    Distance = distance,
                });

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal(2, row.Source);
        Assert.Equal(1, row.Owner);
        Assert.Equal(1, row.Distance);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Guards the verified provider feature pair against full owner scans for one unindexed wide-tree leaf.</summary>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task SelectiveUnindexedAncestorsKeepPhysicalAccessBounded()
    {
        // Arrange
        const int nodes = 100_001;
        const double physicalAccessBudget = 128;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using (var setup = database.CreateContext())
        {
            await CapacitySeed.SeedAsync(setup, nodes);
            await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(setup, Engine);
        }

        var commands = new CompositionCommandProbe();
        var capture = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = database.CreateContext(commands, capture);
        var hasBinding = NestedSetProviderCapabilities.Resolve(context)
            .CompletedSourceMethod is not null;

        var hasOrderedJoin = NestedSetProviderCapabilities.Resolve(context)
            .OrderedJoinMethod is not null;

        var hasOptimizedPath = hasBinding && hasOrderedJoin;
        var ancestorIndex = context.Model.FindEntityType(typeof(TreeNode))!
            .FindAnnotation(NestedSetAnnotationNames.AncestorIndexProperty)
            ?.Value;

        // WHY: This final-leaf range selects the same one source and matches the original capacity workload.
        // Its native access remains observable instead of disappearing into an optimizer-time constant lookup.
        var query = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .SelectAncestors(
                node => node.NodeId >= nodes,
                (source, owner, distance) => new
                {
                    Source = source.NodeId,
                    Owner = owner.NodeId,
                    Distance = distance,
                });

        var constructionCommands = commands.CommandCount;

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);
        var readCommands = commands.CommandCount;
        var materialized = capture.MaterializedNodes;
        var tracked = context
            .ChangeTracker
            .Entries()
            .Count();

        var sql = capture.Commands.Single();

        // WHY: Actual-plan replay is a separate execution. Freeze the completed query's counters first
        // so diagnostic work cannot satisfy or obscure its one-reader and zero-materialization contract.
        var plan = await QueryPlanTestSupport.ExplainAsync(context, capture, 0, Engine);

        // WHY: Attribution deliberately fails closed. Keep the original SQL and native plan available
        // even when an unexpected optimizer shape prevents the work reader from completing.
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-unindexed-ancestor-envelope-point-access-raw",
            [
                $"Nodes={nodes}; sourceBinding={hasBinding}; orderedJoin={hasOrderedJoin}; "
                + $"physicalAccessBudget={physicalAccessBudget}",
                sql,
                plan,
            ]);

        var physical = hasOptimizedPath
            ? AncestorIndexBaseTableWork.ReadUnindexed(sql, plan, Engine)
            : (AncestorIndexBaseTableWork?)null;

        var qualification = physical is { } observed
            ? $"workQualification=available; observedPhysicalTuples={observed.ObservedTuples}; "
            + $"physicalAccesses={observed.PhysicalAccesses}; "
            + $"unexposedPreIndexAccesses={observed.UnexposedPreIndexAccesses}"
            : "workQualification=unavailable; prerequisite=Doka public source-binding and ordered-join APIs; "
            + "semantics are verified without claiming the bounded physical-access budget";

        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-unindexed-ancestor-envelope-point-access",
            [
                $"Nodes={nodes}; sourceBinding={hasBinding}; orderedJoin={hasOrderedJoin}; "
                + $"physicalAccessBudget={physicalAccessBudget}; "
                + $"constructionCommands={constructionCommands}; readCommands={readCommands}; "
                + $"materialized={materialized}; tracked={tracked}; {qualification}",
                sql,
                plan,
            ]);

        // Assert
        Assert.True(hasOptimizedPath);
        var row = Assert.Single(rows);
        Assert.Equal(nodes, row.Source);
        Assert.Equal(1, row.Owner);
        Assert.Equal(1, row.Distance);
        Assert.Equal(0, constructionCommands);
        Assert.Equal(1, readCommands);
        Assert.Equal(0, materialized);
        Assert.Equal(0, tracked);
        Assert.Null(ancestorIndex);

        if (hasOptimizedPath)
        {
            // WHY: One selected wide-tree leaf has exactly one ancestor. This constant allowance covers
            // small lookup overhead while rejecting the former 100,001-row scan, independently of elapsed time.
            var measured = physical
                ?? throw new InvalidOperationException(
                    "The installed source-binding and ordered-join APIs require actual work evidence.");

            Assert.InRange(measured.ObservedTuples, 0, physicalAccessBudget);
        }
    }

    /// <summary>Creates one precomputed structural row without using production bounds to derive the oracle.</summary>
    private static CompositionNode Node(
        Guid scope,
        Guid tree,
        int id,
        int? parent,
        long left,
        long right,
        int depth
    ) => new()
    {
        Scope = scope,
        TreeId = tree,
        Id = id,
        ParentId = parent,
        Left = left,
        Right = right,
        Depth = depth,
    };
}
