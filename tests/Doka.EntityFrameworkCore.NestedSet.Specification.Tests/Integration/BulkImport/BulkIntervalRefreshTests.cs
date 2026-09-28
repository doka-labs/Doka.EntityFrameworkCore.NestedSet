namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies one bounded scalar stream for reserved unordered import intervals.</summary>
public abstract partial class BulkIntervalRefreshTests : ProviderTest
{
    private const int NodeCount = 131;
    private readonly RelationalFixture _assigned;
    private readonly BulkIntervalRefreshFixture _generated;
    private readonly BulkGeneratedFixture _ordered;
    private readonly ITestOutputHelper _output;

    /// <summary>Shares isolated assigned, generated and configured-order mappings with measured test output.</summary>
    protected BulkIntervalRefreshTests(
        IProviderFixture<RelationalFixture> assigned,
        IProviderFixture<BulkIntervalRefreshFixture> generated,
        IProviderFixture<BulkGeneratedFixture> ordered,
        ITestOutputHelper output
    ) : base(assigned)
    {
        _assigned = assigned.Value;
        _generated = generated.Value;
        _ordered = ordered.Value;
        _output = output;
    }

    /// <summary>Assigned imports read one interval and exclude enclosing, adjacent and other-scope rows.</summary>
    [Fact]
    public async Task AssignedSubtreeUsesOneScopedScalarRefresh()
    {
        // Arrange
        var database = await _assigned.ResetAsync(Engine);
        var probe = new BulkIntervalRefreshProbe(nameof(TreeNode));
        await using var context = database.CreateContext(probe);
        await SeedAssignedAsync(context);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var nodes = AssignedNodes();
        var branch = new NestedSetBranch<TreeNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<TreeNode>(node))
                .ToArray());

        // Act
        await tree.InsertSubtreeAsync(branch, 1, CancellationToken.None);
        probe.Inserted = false;

        // Assert
        WriteMeasurement(Engine, probe);
        Assert.Single(probe.Commands);
        Assert.All(probe.Commands, sql => Assert.DoesNotContain(nameof(TreeNode.Payload), sql));
        Assert.Equal((4, 265, 1, 1), (nodes[0].Start, nodes[0].End, nodes[0].Depth, nodes[0].Position));
        Assert.All(nodes.Skip(1), node => Assert.Equal(nodes[0].NodeId, node.Parent));
        Assert.Equal(
            266,
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .Where(node => node.NodeId == 1)
                .Select(node => node.End)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(
            2,
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .Where(node => node.NodeId == 2)
                .Select(node => node.Start)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(
            2,
            await context
                .Set<TreeNode>()
                .CountAsync(node => node.Tree == 2, CancellationToken.None));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Generated keys and computed scalar/complex values survive the single final interval stream.</summary>
    [Fact]
    public async Task GeneratedSubtreeRefreshesComputedValuesInOneStream()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe(nameof(BulkIntervalNode));
        await using var context = await _generated.ResetAsync(Engine, probe);
        var parent = new BulkIntervalNode
        {
            Scope = 1,
            Left = 1,
            Right = 4,
        };

        var foreignRoot = new BulkIntervalNode
        {
            Scope = 2,
            Left = 1,
            Right = 4,
        };

        await context.AddRangeAsync([parent, foreignRoot], CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await context.AddRangeAsync(
            [
                new BulkIntervalNode
                {
                    Scope = 1,
                    ParentId = parent.Id,
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                },
                new BulkIntervalNode
                {
                    Scope = 2,
                    ParentId = foreignRoot.Id,
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                },
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<BulkIntervalNode>()
            .ForScope(1);

        var nodes = Enumerable
            .Range(0, NodeCount)
            .Select(_ => new BulkIntervalNode { Payload = "Retained" })
            .ToArray();

        var branch = new NestedSetBranch<BulkIntervalNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<BulkIntervalNode>(node))
                .ToArray());

        // Act
        await tree.InsertSubtreeAsync(branch, parent.Id, CancellationToken.None);
        probe.Inserted = false;

        // Assert
        WriteMeasurement(Engine, probe);
        Assert.Single(probe.Commands);
        Assert.All(probe.Commands, sql => Assert.DoesNotContain(nameof(BulkIntervalNode.Payload), sql));
        Assert.All(
            nodes,
            node =>
            {
                Assert.True(node.Id > 0);
                Assert.Equal("Retained", node.Payload);
                Assert.Equal("Generated", node.Name);
                Assert.Equal("Generated detail", node.Details.Label);
                Assert.Equal(node.ParentId.GetValueOrDefault() + node.Depth + node.Position, node.Version);
                Assert.Equal(node.Version + 1000, node.Details.Revision);
            });
        Assert.All(nodes.Skip(1), node => Assert.Equal(nodes[0].Id, node.ParentId));
        Assert.Equal((4, 265, 1), (nodes[0].Left, nodes[0].Right, nodes[0].Depth));
        Assert.Equal(
            2,
            await context
                .Set<BulkIntervalNode>()
                .CountAsync(node => node.Scope == 2, CancellationToken.None));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Configured sibling sorting retains bounded keyed refresh after noncontiguous input placement.</summary>
    [Fact]
    public async Task OrderedBranchKeepsKeyedRefreshAfterConfiguredSiblingSorting()
    {
        // Arrange
        var probe = new BulkIntervalRefreshProbe("BulkGeneratedNodes");
        await using var context = await _ordered.ResetAsync(Engine, probe);
        var tree = context
            .NestedSet<BulkGeneratedNode>()
            .ForScope(1);

        await tree.InsertRootAsync(new BulkGeneratedNode { Name = "Root" }, Guid.Empty, CancellationToken.None);
        var parentId = await tree
            .InTree(Guid.Empty)
            .Nodes
            .Select(node => node.Id)
            .SingleAsync(CancellationToken.None);

        await tree.InsertChildAsync(new BulkGeneratedNode { Name = "025 Existing" }, parentId, CancellationToken.None);
        await tree.InsertChildAsync(new BulkGeneratedNode { Name = "095 Existing" }, parentId, CancellationToken.None);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var children = Enumerable
            .Range(0, NodeCount)
            .Reverse()
            .Select(index => new NestedSetBranch<BulkGeneratedNode>(
                new BulkGeneratedNode { Name = FormattableString.Invariant($"{index:D3} New") }))
            .ToArray();

        var branch = new NestedSetBranch<BulkGeneratedNode>(new BulkGeneratedNode { Name = "050 Imported" }, children);

        // Act
        await tree.InsertSubtreeAsync(branch, parentId, CancellationToken.None);
        probe.Inserted = false;

        // Assert
        WriteMeasurement(Engine, probe);
        Assert.Equal(3, probe.Commands.Count);
        Assert.Equal(
            Enumerable
                .Range(0, NodeCount)
                .Reverse()
                .Select(position => (long)position),
            children.Select(child => child.Entity.Position));
        Assert.All(children, child => Assert.Equal(2, child.Entity.Right - child.Entity.Left + 1));
        Assert.Equal(
            3,
            await tree
                .ChildrenOf(parentId)
                .CountAsync(CancellationToken.None));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Records command, read-operation and parameter counts in the central TRX output.</summary>
    private void WriteMeasurement(
        string engine,
        BulkIntervalRefreshProbe probe
    )
    {
        var reads = string.Join(',', probe.ReadCalls);
        var parameters = string.Join(',', probe.ParameterCounts);
        _output.WriteLine(
            FormattableString.Invariant(
                $"{engine}: refresh_queries={probe.Commands.Count}; read_calls={reads}; parameters={parameters}"));
    }

    /// <summary>Creates a branch larger than two existing key batches without allocating distinct payloads.</summary>
    private static TreeNode[] AssignedNodes() => Enumerable
        .Range(10, NodeCount)
        .Select(key => new TreeNode
        {
            NodeId = key,
            Tree = 17,
            Start = 71,
            End = 72,
            Payload = "Retained",
        })
        .ToArray();

    /// <summary>Seeds one root with a neighboring child and an independently valid overlapping scoped tree.</summary>
    private static async Task SeedAssignedAsync(
        TreeContext context
    )
    {
        await context.AddRangeAsync(
            [
                new TreeNode
                {
                    NodeId = 1,
                    Tree = 1,
                    Start = 1,
                    End = 4,
                },
                new TreeNode
                {
                    NodeId = 2,
                    Tree = 1,
                    Parent = 1,
                    Start = 2,
                    End = 3,
                    Depth = 1,
                },
                new TreeNode
                {
                    NodeId = 3,
                    Tree = 2,
                    Start = 1,
                    End = 4,
                },
                new TreeNode
                {
                    NodeId = 4,
                    Tree = 2,
                    Parent = 3,
                    Start = 2,
                    End = 3,
                    Depth = 1,
                },
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }
}
