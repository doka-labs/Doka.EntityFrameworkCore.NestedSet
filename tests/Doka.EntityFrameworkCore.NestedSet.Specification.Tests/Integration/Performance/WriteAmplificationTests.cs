namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Measures structural writes instead of accepting unchanged final values as evidence of low write cost.
/// </summary>
public abstract partial class WriteAmplificationTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses reusable providers while isolating every measured forest.</summary>
    /// <param name="fixture">The fixture owning the real relational databases.</param>
    protected WriteAmplificationTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A local move updates the traversed interval once and never rewrites distant subtrees.</summary>
    [Fact]
    public async Task LocalMoveWritesOnlyTheCrossedInterval()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await service.MoveAfterAsync(2, 4, CancellationToken.None);
        var keys = await service
            .SubtreeOf(1)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.True(probe.BoundsWrites is [{ Rows: 4 } _], probe.Describe());
        Assert.Equal(6, probe.NodeUpdates.Sum(write => write.Rows));
        await AssertCommandBudgetAsync(probe, Engine, 10, "local-move", discoveryRequired: true);
        Assert.Equal([1, 4, 5, 2, 3, 6], keys);
        Assert.Empty(
            (await service
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>
    /// A cross-parent append retains ordinal zero without an empty target shift or unchanged position write.
    /// </summary>
    [Fact]
    public async Task CrossParentMoveSkipsKnownPositionNoOps()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await service.MoveToAsync(2, 6, CancellationToken.None);
        var moved = await service
            .InTree(Guid.Empty)
            .Nodes
            .SingleAsync(node => node.NodeId == 2, CancellationToken.None);

        // Assert
        Assert.Equal(3, probe.NodeUpdates.Count);
        Assert.Equal(8, probe.NodeUpdates.Sum(write => write.Rows));
        Assert.Equal(0, moved.Position);
        Assert.Equal(6, moved.Parent);
        Assert.Empty(
            (await service
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Promotion combines descendant depth repair and both boundary shifts into one row pass.</summary>
    [Fact]
    public async Task PromotedDeleteRewritesEachAffectedBoundaryOnce()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await service.DeleteAsync(2, CancellationToken.None);

        // Assert
        Assert.True(probe.BoundsWrites is [{ Rows: 107 } _], probe.Describe());
        Assert.Equal(108, probe.NodeUpdates.Sum(write => write.Rows));
        await AssertCommandBudgetAsync(probe, Engine, 8, "promoted-delete", discoveryRequired: true);
        Assert.Equal(
            2,
            await service
                .InTree(Guid.Empty)
                .Nodes
                .Where(node => node.NodeId == 3)
                .Select(node => node.Depth)
                .SingleAsync(CancellationToken.None));
        Assert.Empty(
            (await service
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Appending a root does not issue hierarchy updates whose result is already known to be empty.</summary>
    [Fact]
    public async Task RootAppendAvoidsEmptyUpdates()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var treeId = Guid.NewGuid();

        // Act
        await service.InsertRootAsync(new TreeNode { NodeId = 108 }, treeId, CancellationToken.None);

        // Assert
        Assert.True(probe.NodeUpdates.Count == 0, probe.Describe());
        await AssertCommandBudgetAsync(probe, Engine, 6, "new-tree-root", discoveryRequired: false);
        var root = await service
            .InTree(treeId)
            .Nodes
            .SingleAsync(CancellationToken.None);
        Assert.Equal((1L, 2L, 0, 0L), (root.Start, root.End, root.Depth, root.Position));
        Assert.Empty(
            (await service
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>
    /// A child append shifts both coordinates in one pass and skips its empty sibling-position shift.
    /// </summary>
    [Fact]
    public async Task ChildAppendUsesOneBoundaryUpdate()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var probe = new StructuralWriteProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await service.InsertChildAsync(new TreeNode { NodeId = 108 }, 1, CancellationToken.None);

        // Assert
        Assert.True(probe.NodeUpdates.Count == 1 && probe.BoundsWrites is [{ Rows: 103 } _], probe.Describe());
        await AssertCommandBudgetAsync(probe, Engine, 8, "child-append", discoveryRequired: true);
        Assert.DoesNotContain(probe.Commands, sql => sql.Contains("MAX(", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(
            (await service
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Separates one required tree discovery from the unchanged bounded structural command budget.</summary>
    /// <param name="probe">The observed native commands and write counters.</param>
    /// <param name="engine">The provider whose reservation command budget is applied.</param>
    /// <param name="ordinary">The existing structural command ceiling for the operation.</param>
    /// <param name="operation">The stable artifact name for the measured workload.</param>
    /// <param name="discoveryRequired">Whether an anchor must first identify its stable tree.</param>
    /// <returns>A task that completes after saving native SQL and verifying both command categories.</returns>
    private static async Task AssertCommandBudgetAsync(
        StructuralWriteProbe probe,
        string engine,
        int ordinary,
        string operation,
        bool discoveryRequired
    )
    {
        await QueryPlanTestSupport.WriteEvidenceAsync($"{engine}-typed-{operation}-commands", probe.Commands);
        var discoveries = probe
            .Commands
            .Where(IsTreeDiscovery)
            .ToArray();

        if (discoveryRequired)
        {
            Assert.Single(discoveries);
        }
        else
        {
            Assert.Empty(discoveries);
        }

        // WHY: The public anchor identifies its TreeId before locking. Exactly one narrow identity read is
        // necessary; every remaining structural command retains the previously measured operation ceiling.
        var structuralCommands = probe.Commands.Count - discoveries.Length;

        Assert.True(structuralCommands <= CommandBudget(engine, ordinary), probe.Describe());
    }

    /// <summary>Recognizes scalar anchor identity projections without confusing registry or structural reads.</summary>
    private static bool IsTreeDiscovery(
        string command
    )
    {
        var statement = command
            .AsSpan()
            .TrimStart();

        var from = statement.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);

        if (!statement.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || from < 0)
        {
            return false;
        }

        var projection = statement[..from];

        return projection.Contains("TreeId", StringComparison.OrdinalIgnoreCase)
            && !projection.Contains("Start", StringComparison.OrdinalIgnoreCase)
            && !projection.Contains("End", StringComparison.OrdinalIgnoreCase)
            && !projection.Contains("Lifecycle", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds the deterministic Doka revision read needed for a first-use registry reservation.</summary>
    /// <param name="engine">The provider whose SQL command shape is measured.</param>
    /// <param name="ordinary">The upper bound for SQLite, PostgreSQL, and SQL Server.</param>
    /// <returns>The provider-specific command upper bound.</returns>
    private static int CommandBudget(
        string engine,
        int ordinary
    ) =>
        // WHY: MySQL matched-row reporting cannot distinguish an insert from an upsert match. Doka therefore
        // needs one locked revision read; all providers otherwise pay the same first-use tree-registry budget.
        engine is "MySql" or "MariaDb" ? ordinary + 1 : ordinary;
}
