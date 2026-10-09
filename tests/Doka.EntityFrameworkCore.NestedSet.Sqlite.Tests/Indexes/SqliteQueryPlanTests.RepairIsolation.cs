namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class SqliteQueryPlanTests
{
    /// <summary>Seeks composite alternate keys without modifying repeated keys in another scope or tree.</summary>
    [Fact]
    public async Task CompositeRepairBatchUsesPointKeysAndExactMembership()
    {
        // Arrange
        await using var models = new ModelCompatibilityDatabase();
        await using var setup = await models.CreateContextAsync<CompositeKeyContext>(
            Engine,
            static options => new CompositeKeyContext(options));

        var treeId = Guid.NewGuid();
        var excludedTree = Guid.NewGuid();
        var nodes = CompositeForest(1, treeId, 4_097)
            .Concat(CompositeForest(2, treeId, 64))
            .Concat(CompositeForest(1, excludedTree, 16, firstKey: 5_001))
            .ToArray();

        await setup.AddRangeAsync(nodes, CancellationToken.None);
        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        setup.ChangeTracker.Clear();
        var entity = setup.Model.FindEntityType(typeof(CompositeKeyNode))!;
        var probe = new EnterpriseProbe(entity.GetTableName()!);
        await using var context = await models.CreateContextAsync<CompositeKeyContext>(
            Engine,
            static options => new CompositeKeyContext(options),
            probe);

        var store = new Storage.NestedSetStore<CompositeKeyNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(CompositeKeyNode))!,
            1,
            treeId);

        var writer = new Features.Maintenance.NestedSetRepairWriter<CompositeKeyNode, int, Guid, int>(store);

        // WHY: The same batch deliberately includes existing keys from an excluded tree, while another
        // scope repeats every requested target key. A point lookup must still enforce both identity parts.
        var repairs = Enumerable
            .Range(1, 48)
            .Concat(Enumerable.Range(5_001, 16))
            .Select(key => new Features.Maintenance.NestedSetRepair<int>(key, 100_001, 100_002, 7, 9))
            .ToArray();

        // Act
        var affected = await writer.WriteAsync(repairs, 0, CancellationToken.None);

        // Assert
        Assert.Equal(48, affected);
        var persisted = await context
            .Set<CompositeKeyNode>()
            .AsNoTracking()
            .ToArrayAsync(CancellationToken.None);

        foreach (var expected in nodes)
        {
            var actual = persisted.Single(node => node.TenantId == expected.TenantId && node.RowId == expected.RowId);
            var changed = expected.TenantId == 1 && expected.TreeId == treeId && expected.NodeKey <= 48;
            Assert.Equal(
                changed
                    ? (100_001L, 100_002L, 7, 9L)
                    : (expected.Left, expected.Right, expected.Depth, expected.Position),
                (actual.Left, actual.Right, actual.Depth, actual.Position));
            Assert.Equal(
                (expected.TreeId, expected.ParentNodeKey, expected.Name),
                (actual.TreeId, actual.ParentNodeKey, actual.Name));
        }

        var plan = await ExplainAsync(context, probe, 0);
        AssertPointKeyPlan(plan, entity, nameof(CompositeKeyNode.NodeKey), nameof(CompositeKeyNode.TenantId));
        await QueryPlanTestSupport.WriteEvidenceAsync("Sqlite-composite-repair-point-plan", plan);
        await QueryPlanTestSupport.WriteEvidenceAsync("Sqlite-composite-repair-update", [probe.Commands[0]]);
    }

    /// <summary>Seeks converted unscoped keys while rejecting keys that belong to another tree.</summary>
    [Fact]
    public async Task ConvertedUnscopedRepairBatchUsesPointKeysAndExactTree()
    {
        // Arrange
        await using var models = new ModelCompatibilityDatabase();
        await using var setup = await models.CreateContextAsync<StrictStrongIdContext>(
            Engine,
            static options => new StrictStrongIdContext(options));

        var treeId = Guid.NewGuid();
        var excludedTree = Guid.NewGuid();
        var nodes = ConvertedForest(treeId, 4_097)
            .Concat(ConvertedForest(excludedTree, 16, firstKey: 5_001))
            .ToArray();

        await setup.AddRangeAsync(nodes, CancellationToken.None);
        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        setup.ChangeTracker.Clear();
        var entity = setup.Model.FindEntityType(typeof(OrderedStrongIdNode))!;
        var probe = new EnterpriseProbe(entity.GetTableName()!);
        await using var context = await models.CreateContextAsync<StrictStrongIdContext>(
            Engine,
            static options => new StrictStrongIdContext(options),
            probe);

        var store = new Storage.NestedSetStore<OrderedStrongIdNode, StrongNodeId, Guid, Mapping.NestedSetNoScope>(
            context,
            context.Model.FindEntityType(typeof(OrderedStrongIdNode))!,
            default,
            treeId);

        var writer =
            new Features.Maintenance.NestedSetRepairWriter<OrderedStrongIdNode, StrongNodeId, Guid,
                Mapping.NestedSetNoScope>(store);

        var repairs = Enumerable
            .Range(1, 48)
            .Concat(Enumerable.Range(5_001, 16))
            .Select(key => new Features.Maintenance.NestedSetRepair<StrongNodeId>(
                new StrongNodeId(key),
                100_001,
                100_002,
                7,
                9))
            .ToArray();

        // Act
        var affected = await writer.WriteAsync(repairs, 0, CancellationToken.None);

        // Assert
        Assert.Equal(48, affected);
        var persisted = await context
            .Set<OrderedStrongIdNode>()
            .AsNoTracking()
            .ToArrayAsync(CancellationToken.None);

        foreach (var expected in nodes)
        {
            var actual = persisted.Single(node => node.Id == expected.Id);
            var changed = expected.TreeId == treeId && expected.Id.Value <= 48;
            Assert.Equal(
                changed
                    ? (100_001L, 100_002L, 7, 9L)
                    : (expected.Left, expected.Right, expected.Depth, expected.Position),
                (actual.Left, actual.Right, actual.Depth, actual.Position));
            Assert.Equal(
                (expected.TreeId, expected.ParentId, expected.Name),
                (actual.TreeId, actual.ParentId, actual.Name));
        }

        var plan = await ExplainAsync(context, probe, 0);
        AssertPointKeyPlan(plan, entity, nameof(OrderedStrongIdNode.Id));
        await QueryPlanTestSupport.WriteEvidenceAsync("Sqlite-converted-unscoped-repair-point-plan", plan);
        await QueryPlanTestSupport.WriteEvidenceAsync("Sqlite-converted-unscoped-repair-update", [probe.Commands[0]]);
    }

    /// <summary>Preserves exact membership when a mapped table collides with the internal query alias.</summary>
    [Fact]
    public async Task MembershipAliasCannotShadowTheMappedTable()
    {
        // Arrange
        await using var models = new ModelCompatibilityDatabase();
        await using var setup = await models.CreateContextAsync<MembershipCollisionContext>(
            Engine,
            static options => new MembershipCollisionContext(options));
        var treeId = Guid.NewGuid();
        var nodes = new[]
        {
            new TreeNode
            {
                NodeId = 1,
                Tree = 1,
                TreeId = treeId,
                Start = 1,
                End = 2,
            },
            new TreeNode
            {
                NodeId = 2,
                Tree = 1,
                TreeId = Guid.NewGuid(),
                Start = 1,
                End = 2,
            },
            new TreeNode
            {
                NodeId = 3,
                Tree = 2,
                TreeId = treeId,
                Start = 1,
                End = 2,
            },
        };

        await setup.AddRangeAsync(nodes, CancellationToken.None);
        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        setup.ChangeTracker.Clear();
        var entity = setup.Model.FindEntityType(typeof(TreeNode))!;
        var probe = new EnterpriseProbe(entity.GetTableName()!);
        await using var context = await models.CreateContextAsync<MembershipCollisionContext>(
            Engine,
            static options => new MembershipCollisionContext(options),
            probe);

        var store = new Storage.NestedSetStore<TreeNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(TreeNode))!,
            1,
            treeId);

        var writer = new Features.Maintenance.NestedSetRepairWriter<TreeNode, int, Guid, int>(store);
        var repairs = nodes
            .Select(node => new Features.Maintenance.NestedSetRepair<int>(node.NodeId, 101, 102, 7, 9))
            .ToArray();

        // Act
        var affected = await writer.WriteAsync(repairs, 0, CancellationToken.None);

        // Assert
        Assert.Equal(1, affected);
        var persisted = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);
        Assert.Equal(
            (101L, 102L, 7, 9L),
            (persisted[0].Start, persisted[0].End, persisted[0].Depth, persisted[0].Position));
        Assert.Equal(
            nodes
                .Skip(1)
                .Select(node => (node.NodeId, node.Tree, node.TreeId, node.Start, node.End)),
            persisted
                .Skip(1)
                .Select(node => (node.NodeId, node.Tree, node.TreeId, node.Start, node.End)));
        Assert.All(persisted.Skip(1), node => Assert.Equal((0, 0L), (node.Depth, node.Position)));
        await QueryPlanTestSupport.WriteEvidenceAsync("Sqlite-membership-alias-update", [probe.Commands[0]]);
    }

    /// <summary>Creates a canonical scoped star with independent row and scalar node identities.</summary>
    private static IEnumerable<CompositeKeyNode> CompositeForest(
        int scope,
        Guid treeId,
        int count,
        int firstKey = 1
    ) => Enumerable
        .Range(0, count)
        .Select(index => new CompositeKeyNode
        {
            TenantId = scope,
            RowId = firstKey + index,
            NodeKey = firstKey + index,
            TreeId = treeId,
            ParentNodeKey = index == 0 ? null : firstKey,
            Left = index == 0 ? 1 : index * 2L,
            Right = index == 0 ? count * 2L : (index * 2L) + 1,
            Depth = index == 0 ? 0 : 1,
            Position = index == 0 ? 0 : index - 1,
        });

    /// <summary>Creates canonical converted identities without performing measured public insertion work.</summary>
    private static IEnumerable<OrderedStrongIdNode> ConvertedForest(
        Guid treeId,
        int count,
        int firstKey = 1
    ) => Enumerable
        .Range(0, count)
        .Select(index => new OrderedStrongIdNode
        {
            Id = new StrongNodeId(firstKey + index),
            TreeId = treeId,
            ParentId = index == 0 ? null : new StrongNodeId(firstKey),
            Left = index == 0 ? 1 : index * 2L,
            Right = index == 0 ? count * 2L : (index * 2L) + 1,
            Depth = index == 0 ? 0 : 1,
            Position = index == 0 ? 0 : index - 1,
        });

    /// <summary>Checks the UPDATE's outer lookup uses the model's key rather than only a tree range.</summary>
    private static void AssertPointKeyPlan(
        List<string> plan,
        IEntityType entity,
        string keyName,
        string? scopeName = null
    )
    {
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var key = entity.FindProperty(keyName)!.GetColumnName(table);
        var predicate = scopeName is null
            ? $"{key}=?"
            : $"{entity.FindProperty(scopeName)!.GetColumnName(table)}=? AND {key}=?";

        // WHY: EXPLAIN distinguishes the bounded outer key lookup from its per-key membership probe.
        // Index names are implementation details; mapped equality terms establish the access contract.
        Assert.NotEmpty(plan);
        Assert.True(
            plan[0]
                .Contains(predicate, StringComparison.Ordinal)
            || (plan[0]
                    .Contains("USING INTEGER PRIMARY KEY", StringComparison.Ordinal)
                && plan[0]
                    .Contains("rowid=?", StringComparison.Ordinal)),
            string.Join("\n", plan));
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN ", StringComparison.Ordinal));
    }

    /// <summary>Maps a deliberate case-insensitive collision with the membership query's preferred alias.</summary>
    private sealed class MembershipCollisionContext : DbContext
    {
        /// <summary>Creates an isolated SQLite model for the alias-shadowing adversarial case.</summary>
        internal MembershipCollisionContext(
            DbContextOptions<MembershipCollisionContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<TreeNode>();
            // WHY: SQLite identifier equality ignores casing; different spelling must not evade alias avoidance.
            node.ToTable("NeStEdSeTmEmBeRsHiP");
            node.HasKey(value => value.NodeId);
            node
                .Property(value => value.NodeId)
                .ValueGeneratedNever();
            node.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(value => value.NodeId)
                .HasScope(value => value.Tree)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.Parent)
                .HasBounds(value => value.Start, value => value.End)
                .HasDepth(value => value.Depth)
                .HasPosition(value => value.Position));
        }
    }
}
