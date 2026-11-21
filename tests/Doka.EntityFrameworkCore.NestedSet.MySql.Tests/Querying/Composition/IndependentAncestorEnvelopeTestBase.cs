using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Preserves unindexed ancestor semantics when a completed independent source narrows candidates.</summary>
public abstract partial class IndependentAncestorEnvelopeTestBase : ProviderTest
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
    public async Task SelectAncestors_EmptySelection_ReturnsNoAncestors()
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

    /// <summary>A hidden mapped owner remains invisible after coordinate filtering and payload hydration.</summary>
    [Fact]
    public async Task SelectAncestors_HiddenAncestor_IsNotReintroducedByTheEnvelope()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context =
            await CompositionTestData.CreateContextAsync(
                _fixture,
                Engine,
                scope,
                probe,
                hidden: 2,
                cancellationToken: TestContext.Current.CancellationToken,
                closed: null,
                planProbe: null,
                chainNodes: null,
                ownerMode: CompositionTestData.ChainOwnerMode.RootValue);

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

    /// <summary>
    /// The statement contains one server-volatile source selection and preserves its actual complete result.
    /// </summary>
    [Fact]
    public async Task SelectAncestors_VolatileSelection_IsNotCopiedIntoTheEnvelope()
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
                .Split("RAND()")
                .Length
            - 1);

        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Guards the verified provider feature pair against full owner scans for one unindexed wide-tree leaf.
    /// </summary>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task ExplainAsync_SelectiveUnindexedAncestors_KeepPhysicalAccessBounded()
    {
        // Arrange
        const int nodes = 100_001;
        const double physicalAccessBudget = 128;
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (var setup = database.CreateContext())
        {
            await CapacitySeed.SeedAsync(
                setup,
                nodes,
                cancellationToken: TestContext.Current.CancellationToken,
                deep: false,
                corrupt: false);
            await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(
                setup,
                Engine,
                cancellationToken: TestContext.Current.CancellationToken);
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
        var plan = await QueryPlanTestSupport.ExplainAsync(
            context,
            capture,
            0,
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        // WHY: Attribution deliberately fails closed. Keep the original SQL and native plan available
        // even when an unexpected optimizer shape prevents the work reader from completing.
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-unindexed-ancestor-envelope-point-access-raw",
            [
                $"Nodes={nodes}; sourceBinding={hasBinding}; orderedJoin={hasOrderedJoin}; "
                + $"physicalAccessBudget={physicalAccessBudget}",
                sql,
                plan,
            ],
            cancellationToken: TestContext.Current.CancellationToken);

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
            ],
            cancellationToken: TestContext.Current.CancellationToken);

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
