using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Seeds native populations without tracking or buffering one CLR entity per hierarchy row.</summary>
/// <remarks>
/// This query-only fixture follows CapacitySeed's raw population convention. It qualifies physical adjacency
/// and coordinates, not complete mutation-registry row-count metadata or a full validator result.
/// </remarks>
internal static class DensePathWorkFixture
{
    /// <summary>Creates the independent adjacency plan and bounded payload used by one native work case.</summary>
    internal static async Task SeedAsync(
        TreeContext context,
        DensePathWorkScenario scenario,
        string engine
    )
    {
        await CapacitySeed.SeedAsync(context, scenario.Nodes, deep: !scenario.Branches);

        if (scenario.Branches)
        {
            await ArrangeBranchesAsync(context, scenario.Nodes);
        }

        if (scenario.Branches
            || scenario.Forest)
        {
            await CloneAsync(context, scenario.Nodes, 1, DensePathWorkScenario.OtherTree);
            await CloneAsync(context, scenario.Nodes, 3, Guid.Empty);
        }

        if (scenario is { Forest: true, Branches: false })
        {
            // WHY: Both authorized chains must reach the prior-block candidate. Only the foreign-scope
            // clone stays blocked, so coordinate overlap cannot supply or suppress an authorized path.
            await context
                .Set<TreeNode>()
                .Where(node => node.Tree == 1 && node.TreeId == DensePathWorkScenario.OtherTree)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.Payload, "clear"),
                    CancellationToken.None);
        }

        if (scenario.DenseFailures)
        {
            await context
                .Set<TreeNode>()
                .Where(node =>
                    node.Tree == 1
                    && node.TreeId == Guid.Empty
                    && node.NodeId <= scenario.Nodes / 2
                    && node.NodeId % 2 == 0)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.Payload, "blocked"),
                    CancellationToken.None);
        }

        if (scenario.ExtraFailure is { } failure)
        {
            await context
                .Set<TreeNode>()
                .Where(node => node.NodeId == failure)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.Payload, "blocked"),
                    CancellationToken.None);
        }

        if (scenario.Branches)
        {
            var allPairs = scenario with { Forest = true };
            var uppers = allPairs
                .Pairs()
                .Select(pair => pair.Upper)
                .Distinct()
                .ToArray();

            await context
                .Set<TreeNode>()
                .Where(node => Enumerable.Contains(uppers, node.NodeId))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.Payload, "upper"),
                    CancellationToken.None);

            await context
                .Set<UnrelatedRow>()
                .AddRangeAsync(
                    allPairs
                        .Pairs()
                        .Select(pair => new UnrelatedRow
                        {
                            Id = pair.Lower,
                            Value = "fact",
                        }),
                    CancellationToken.None);

            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
        }
        else
        {
            await CopyPayloadAsync(context, scenario.StoredNodes);
        }

        await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(context, engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<UnrelatedRow>(context, engine);
    }

    /// <summary>Transforms the existing wide fixture into one hundred disjoint chains under its root.</summary>
    private static async Task ArrangeBranchesAsync(
        TreeContext context,
        int nodes
    )
    {
        var map = Mapping(context);
        var id = map.Column(nameof(TreeNode.NodeId));
        var branch = $"(({id} - 2) DIV {DensePathWorkScenario.BranchSize})";
        var offset = $"MOD({id} - 2, {DensePathWorkScenario.BranchSize})";
        var first = $"(2 + ({branch} * {DensePathWorkScenario.BranchSize}))";
        var length = $"LEAST({DensePathWorkScenario.BranchSize}, {nodes} - {first} + 1)";
        var left = $"(2 + (2 * {DensePathWorkScenario.BranchSize} * {branch}) + {offset})";
        var right = $"(2 + (2 * {DensePathWorkScenario.BranchSize} * {branch}) + (2 * {length}) - {offset} - 1)";
        var statement = $"UPDATE {map.Table} SET "
            + $"{map.Column(nameof(TreeNode.Parent))} = CASE WHEN {offset} = 0 THEN 1 ELSE {id} - 1 END, "
            + $"{map.Column(nameof(TreeNode.Start))} = {left}, {map.Column(nameof(TreeNode.End))} = {right}, "
            + $"{map.Column(nameof(TreeNode.Depth))} = {offset} + 1, "
            + $"{map.Column(nameof(TreeNode.Position))} = CASE WHEN {offset} = 0 THEN {branch} ELSE 0 END "
            + $"WHERE {id} > 1 AND {id} <= {nodes}";

        // WHY: DIV is the two owned engines' integer operator; no floating division or CLR tree buffer is involved.
        await context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);
    }

    /// <summary>Creates overlapping coordinates with an independently registered scope/tree identity.</summary>
    private static async Task CloneAsync(
        TreeContext context,
        int nodes,
        int scope,
        Guid tree
    )
    {
        var offset = scope == 1 ? nodes : 2 * nodes;
        await context
            .NestedSet<TreeNode>()
            .ForScope(scope)
            .InsertRootAsync(
                new TreeNode
                {
                    NodeId = offset + 1,
                    Payload = "blocked",
                },
                tree,
                CancellationToken.None);

        context.ChangeTracker.Clear();
        await context
            .Set<TreeNode>()
            .Where(node => node.NodeId == offset + 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.End, 2L * nodes), CancellationToken.None);

        var map = Mapping(context);
        var names = new[]
        {
            nameof(TreeNode.NodeId),
            nameof(TreeNode.Tree),
            nameof(TreeNode.TreeId),
            nameof(TreeNode.Parent),
            nameof(TreeNode.Start),
            nameof(TreeNode.End),
            nameof(TreeNode.Depth),
            nameof(TreeNode.Position),
            nameof(TreeNode.Payload),
        };

        var columns = string.Join(", ", names.Select(map.Column));
        var sourceId = map.Column(nameof(TreeNode.NodeId));
        var select = $"{sourceId} + {offset}, {scope}, @tree, "
            + $"{map.Column(nameof(TreeNode.Parent))} + {offset}, "
            + string.Join(", ", names.Skip(4).Take(4).Select(map.Column))
            + ", 'blocked'";

        var statement = $"INSERT INTO {map.Table} ({columns}) SELECT {select} FROM {map.Table} "
            + $"WHERE {sourceId} > 1 AND {sourceId} <= {nodes} ORDER BY {sourceId}";

        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        var parameter = map.Entity.FindProperty(nameof(TreeNode.TreeId))!
            .GetRelationalTypeMapping()
            .CreateParameter(command, "tree", tree, nullable: false);

        // WHY: Mapped native bindings retain Doka's binary GUID encoding for the printable-byte second TreeId.
        await context.Database.ExecuteSqlRawAsync(statement, new object[] { parameter }, CancellationToken.None);
    }

    /// <summary>Retains the original chain payload layout using one native copy rather than tracked entities.</summary>
    private static async Task CopyPayloadAsync(
        TreeContext context,
        int nodes
    )
    {
        var map = Mapping(context);
        var target = context.Model.FindEntityType(typeof(UnrelatedRow))!;
        var store = StoreObjectIdentifier.Table(target.GetTableName()!, target.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var key = sql.DelimitIdentifier(target.FindProperty(nameof(UnrelatedRow.Id))!.GetColumnName(store)!);
        var value = sql.DelimitIdentifier(target.FindProperty(nameof(UnrelatedRow.Value))!.GetColumnName(store)!);
        var id = map.Column(nameof(TreeNode.NodeId));
        var statement = $"INSERT INTO {sql.DelimitIdentifier(store.Name, store.Schema)} ({key}, {value}) "
            + $"SELECT {id}, 'fact' FROM {map.Table} "
            + $"WHERE {id} <= {nodes.ToString(CultureInfo.InvariantCulture)}";

        await context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);
    }

    /// <summary>Resolves exact physical identifiers once for each bounded native fixture operation.</summary>
    private static NativeMap Mapping(
        TreeContext context
    )
    {
        var entity = context.Model.FindEntityType(typeof(TreeNode))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

        return new NativeMap(entity, store, context.GetService<ISqlGenerationHelper>());
    }

    /// <summary>Keeps native SQL identifiers tied to the actual fixture's EF mapping.</summary>
    /// <param name="Entity">The actual TreeNode metadata.</param>
    /// <param name="Store">The mapped physical table identity.</param>
    /// <param name="Sql">The concrete provider's identifier helper.</param>
    private sealed record NativeMap(
        IEntityType Entity,
        StoreObjectIdentifier Store,
        ISqlGenerationHelper Sql
    )
    {
        /// <summary>Gets the delimited physical fixture table.</summary>
        internal string Table => Sql.DelimitIdentifier(Store.Name, Store.Schema);

        /// <summary>Gets one delimited physical column by its mapped CLR property.</summary>
        internal string Column(
            string name
        ) => Sql.DelimitIdentifier(Entity.FindProperty(name)!.GetColumnName(Store)!);
    }
}
