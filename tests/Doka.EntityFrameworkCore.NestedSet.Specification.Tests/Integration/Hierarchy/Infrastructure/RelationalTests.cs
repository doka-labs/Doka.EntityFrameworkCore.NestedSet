namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies each nested-set operation independently against the supported relational engines.</summary>
/// <remarks>
///     Cases share a class-owned database, but each case starts with empty tables and lock rows.
/// </remarks>
public abstract partial class RelationalTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Creates a case using the shared, sequentially accessed database fixture.</summary>
    /// <param name="fixture">The fixture that owns one database per requested engine.</param>
    protected RelationalTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Creates an untracked node whose structural values will be assigned by the service.</summary>
    private static TreeNode Node(
        int id
    ) => new() { NodeId = id };

    /// <summary>Seeds siblings and a grandchild without performing assertions during Arrange.</summary>
    private static async Task SeedPlacementTreeAsync(
        TreeContext context,
        int scope
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: CancellationToken.None);
        await tree.InsertAsFirstChildAsync(Node(3), 1, CancellationToken.None);
        await tree.InsertBeforeAsync(Node(4), 2, CancellationToken.None);
        await tree.InsertAfterAsync(Node(5), 2, CancellationToken.None);
        await tree.InsertChildAsync(Node(6), 2, cancellationToken: CancellationToken.None);
    }

    /// <summary>Seeds two branches whose unequal depths expose subtree-level depth errors.</summary>
    private static async Task SeedDeepTreeAsync(
        TreeContext context,
        int scope
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(3), 2, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(4), 3, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(5), 4, cancellationToken: CancellationToken.None);
        await tree.InsertRootAsync(Node(6), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(7), 6, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(8), 7, cancellationToken: CancellationToken.None);
    }

    /// <summary>
    ///     Seeds branching children so repair checks nonzero positions in multiple sibling groups.
    /// </summary>
    private static async Task SeedRebuildTreeAsync(
        TreeContext context,
        int scope
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(3), 1, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(4), 2, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(5), 4, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(6), 1, CancellationToken.None);
    }

    /// <summary>Seeds multiple child subtrees between surrounding siblings for promotion tests.</summary>
    private static async Task SeedPromotionTreeAsync(
        TreeContext context,
        int scope
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await SeedDeepTreeAsync(context, scope);
        await tree.InsertBeforeAsync(Node(9), 3, CancellationToken.None);
        await tree.InsertAfterAsync(Node(10), 3, CancellationToken.None);
        await tree.InsertAfterAsync(Node(11), 4, CancellationToken.None);
        await tree.InsertChildAsync(Node(12), 11, cancellationToken: CancellationToken.None);
    }

    /// <summary>Seeds a three-node subtree and a deeper destination for executed-depth rollback probes.</summary>
    private static async Task SeedDepthFailureTreeAsync(
        TreeContext context,
        int scope
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(3), 2, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(Node(4), 3, cancellationToken: CancellationToken.None);
        await tree.InsertRootAsync(Node(6), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(7), 6, cancellationToken: CancellationToken.None);
    }

    /// <summary>Seeds a branch whose parent links are protected by an immediate restrictive foreign key.</summary>
    private static async Task SeedConstrainedTreeAsync(
        TreeContext context,
        int scope
    )
    {
        var tree = context
            .NestedSet<ConstrainedNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(new ConstrainedNode { Id = 1 }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new ConstrainedNode { Id = 2 }, 1, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(new ConstrainedNode { Id = 3 }, 2, cancellationToken: CancellationToken.None);
        await tree.InsertChildAsync(new ConstrainedNode { Id = 4 }, 1, cancellationToken: CancellationToken.None);
    }

    /// <summary>Delays an independent tree creation until both contexts are ready.</summary>
    private static async Task InsertAfterGateAsync(
        ScopedNestedSet<TreeNode, int> tree,
        int id,
        Task gate
    )
    {
        await gate;
        await tree.InsertRootAsync(Node(id), Guid.NewGuid(), CancellationToken.None);
    }

    /// <summary>Dispatches exactly one mutation selected by a scope-isolation theory case.</summary>
    private static Task ApplyScopedMutationAsync(
        TreeContext context,
        int scope,
        string operation
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        return operation switch
        {
            "InsertRoot" => tree.InsertRootAsync(Node(7), Guid.NewGuid(), CancellationToken.None),
            "InsertFirstChild" => tree.InsertAsFirstChildAsync(Node(7), 1, CancellationToken.None),
            "InsertLastChild" => tree.InsertAsLastChildAsync(Node(7), 1, CancellationToken.None),
            "InsertBefore" => tree.InsertBeforeAsync(Node(7), 2, CancellationToken.None),
            "InsertAfter" => tree.InsertAfterAsync(Node(7), 2, CancellationToken.None),
            "MoveBefore" => tree.MoveBeforeAsync(2, 3, CancellationToken.None),
            "MoveAfter" => tree.MoveAfterAsync(2, 5, CancellationToken.None),
            "MoveFirstChild" => tree.MoveBeforeAsync(5, 6, CancellationToken.None),
            "MoveLastChild" => tree.MoveToAsync(3, 2, CancellationToken.None),
            "DetachAsTree" => tree.DetachAsTreeAsync(2, Guid.NewGuid(), CancellationToken.None),
            "Delete" => tree.DeleteAsync(2, CancellationToken.None),
            "DeleteSubtree" => tree.DeleteSubtreeAsync(2, CancellationToken.None),
            "Rebuild" => tree
                .InTree(Guid.Empty)
                .RebuildAsync(CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    /// <summary>Corrupts derived values while retaining the ordered adjacency used by repair.</summary>
    private static Task<int> CorruptStructureAsync(
        TreeContext context,
        int scope,
        string corruption
    )
    {
        var nodes = context
            .NestedSet<TreeNode>()
            .ForScope(scope)
            .InTree(Guid.Empty)
            .Nodes;

        return corruption switch
        {
            "Bounds" => nodes.ExecuteUpdateAsync(
                x => x
                    .SetProperty(n => n.Start, 1)
                    .SetProperty(n => n.End, 2),
                CancellationToken.None),
            "NegativeDepth" => nodes.ExecuteUpdateAsync(x => x.SetProperty(n => n.Depth, 0), CancellationToken.None),
            "ExcessiveDepth" => nodes.ExecuteUpdateAsync(x => x.SetProperty(n => n.Depth, 77), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
    }

    /// <summary>Creates one unrepairable adjacency defect without testing it during Arrange.</summary>
    private static Task<int> CorruptAdjacencyAsync(
        TreeContext context,
        int scope,
        string engine,
        string corruption
    )
    {
        var nodes = context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == scope);

        return corruption switch
        {
            "Cycle" => nodes
                .Where(x => x.NodeId == 1)
                .ExecuteUpdateAsync(x => x.SetProperty(n => n.Parent, 2), CancellationToken.None),
            "Orphan" => CorruptOrphanAsync(context, scope, engine),
            "DuplicateChildPosition" => nodes
                .Where(x => x.NodeId == 3)
                .ExecuteUpdateAsync(x => x.SetProperty(n => n.Position, 0), CancellationToken.None),
            "MultipleRoots" => nodes
                .Where(x => x.NodeId == 6)
                .ExecuteUpdateAsync(
                    x => x
                        .SetProperty(n => n.Position, 0)
                        .SetProperty(n => n.Parent, (int?)null),
                    CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
    }

    /// <summary>Creates external orphan corruption while restoring normal FK enforcement before Rebuild.</summary>
    private static async Task<int> CorruptOrphanAsync(
        TreeContext context,
        int scope,
        string engine
    )
    {
        var entity = context.Model.FindEntityType(typeof(TreeNode))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        var disable = engine switch
        {
            "Sqlite" => "PRAGMA foreign_keys = OFF",
            "MySql" or "MariaDb" => "SET FOREIGN_KEY_CHECKS = 0",
            "PostgreSql" => $"ALTER TABLE {table} DISABLE TRIGGER ALL",
            "SqlServer" => $"ALTER TABLE {table} NOCHECK CONSTRAINT ALL",
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        var enable = engine switch
        {
            "Sqlite" => "PRAGMA foreign_keys = ON",
            "MySql" or "MariaDb" => "SET FOREIGN_KEY_CHECKS = 1",
            "PostgreSql" => $"ALTER TABLE {table} ENABLE TRIGGER ALL",
            "SqlServer" => $"ALTER TABLE {table} CHECK CONSTRAINT ALL",
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        var wasOpen = context.Database.GetDbConnection().State == ConnectionState.Open;
        if (!wasOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        try
        {
            // WHY: The production model correctly prevents orphans. Rebuild must still diagnose externally
            // corrupted or adopted databases, so this test bypasses the FK only for the corrupting statement.
            await context.Database.ExecuteSqlRawAsync(disable, CancellationToken.None);

            return await context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == scope)
                .Where(node => node.NodeId == 2)
                .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Parent, -1), CancellationToken.None);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(enable, CancellationToken.None);

            if (!wasOpen)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>Compares the complete ordered result of a hierarchy query.</summary>
    private static async Task AssertOrderAsync(
        IQueryable<TreeNode> query,
        params int[] expected
    ) => Assert.Equal(
        expected,
        await query
            .Select(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None));

    /// <summary>Checks selected depth values while keeping all assertions in the assertion phase.</summary>
    private static async Task AssertDepthsAsync(
        TreeContext context,
        int scope,
        params (int Id, int Depth)[] expected
    )
    {
        foreach (var (id, depth) in expected)
        {
            var actual = await context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == scope)
                .Where(x => x.NodeId == id)
                .Select(x => x.Depth)
                .SingleAsync(CancellationToken.None);

            Assert.Equal(depth, actual);
        }
    }

    /// <summary>Captures every structural value in key order for rollback and scope-isolation comparisons.</summary>
    private static async Task<string[]> SnapshotAsync(
        TreeContext context,
        int scope
    ) => (await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == scope)
            .OrderBy(x => x.NodeId)
            .ToListAsync(CancellationToken.None))
        .Select(x => $"{x.NodeId}:{x.Start}:{x.End}:{x.Parent}:{x.Depth}:{x.Position}:{x.Tree}:{x.TreeId}")
        .ToArray();

    /// <summary>
    ///     Checks validation output and independently recomputes interval, parent, depth and position invariants.
    /// </summary>
    private static async Task AssertValidAsync(
        TreeContext context,
        int scope
    )
    {
        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == scope)
            .OrderBy(node => node.Start)
            .ToArrayAsync(CancellationToken.None);

        // WHY: Bounds and positions are local to TreeId. Equal coordinates in independent trees are intentional.
        foreach (var group in nodes.GroupBy(node => node.TreeId))
        {
            var treeNodes = group
                .OrderBy(node => node.Start)
                .ToArray();

            var report = await context
                .NestedSet<TreeNode>()
                .ForScope(scope)
                .InTree(group.Key)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

            Assert.True(report.IsValid, string.Join("; ", report.Issues.Select(issue => issue.Message)));
            Assert.Single(treeNodes, node => node.Parent == null);
            Assert.Equal(
                Enumerable
                    .Range(1, treeNodes.Length * 2)
                    .Select(value => (long)value),
                treeNodes
                    .SelectMany(node => new[] { node.Start, node.End })
                    .Order());

            foreach (var node in treeNodes)
            {
                var ancestors = treeNodes
                    .Where(parent => parent.Start < node.Start && parent.End > node.End)
                    .ToArray();

                Assert.True(node.Start < node.End);
                Assert.Equal(0, (node.End - node.Start + 1) % 2);
                Assert.Equal(ancestors.LastOrDefault()?.NodeId, node.Parent);
                Assert.Equal(ancestors.Length, node.Depth);
            }

            foreach (var siblings in treeNodes.GroupBy(node => node.Parent))
            {
                Assert.Equal(
                    Enumerable
                        .Range(0, siblings.Count())
                        .Select(value => (long)value),
                    siblings.Select(node => node.Position));
            }
        }
    }
}
