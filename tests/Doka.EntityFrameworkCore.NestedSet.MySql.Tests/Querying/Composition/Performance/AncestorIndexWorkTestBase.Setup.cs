using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Seeds independently derived stars or chains without tracking a population of CLR entities.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="scope">The hierarchy scope used by the scenario.</param>
    /// <param name="tree">The complete tree identity used by the scenario.</param>
    /// <param name="forest">Whether the input spans multiple independent trees.</param>
    /// <param name="dense">Whether the requested anchors cover the dense path.</param>
    /// <param name="branching">The deterministic branch factor.</param>
    /// <param name="balanced">Whether the synthetic hierarchy branches instead of forming a chain.</param>
    /// <param name="nodes">The number of synthetic hierarchy nodes.</param>
    private static async Task SeedAsync(
        AncestorIndexQueryContext context,
        Guid scope,
        Guid tree,
        bool forest,
        bool dense,
        bool branching,
        bool balanced,
        int nodes,
        CancellationToken cancellationToken
    )
    {
        var entity = context.Model.FindEntityType(typeof(CompositionNavigationNode))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var helper = context.GetService<ISqlGenerationHelper>();

        var caption = helper.DelimitIdentifier(
            entity.FindNavigation(nameof(CompositionNavigationNode.Details))!.TargetEntityType.FindProperty(
                nameof(CompositionNavigationDetail.Caption))!.GetColumnName(store)!);

        var columns = string.Join(
            ", ",
            new[]
                {
                    nameof(CompositionNavigationNode.Scope),
                    nameof(CompositionNavigationNode.Id),
                    nameof(CompositionNavigationNode.TreeId),
                    nameof(CompositionNavigationNode.ParentId),
                    nameof(CompositionNavigationNode.Left),
                    nameof(CompositionNavigationNode.Right),
                    nameof(CompositionNavigationNode.Depth),
                    nameof(CompositionNavigationNode.Position),
                    nameof(CompositionNavigationNode.Visible),
                }
                .Select(Column)
                .Append(caption));

        var table = helper.DelimitIdentifier(store.Name, store.Schema);
        await using var parameterCommand = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        nodes = branching || balanced
            ? 100_001
            : nodes;

        var balancedCoordinates = balanced
            ? BalancedCoordinates(nodes)
            : default;

        for (var start = 1; start <= nodes; start += 1_000)
        {
            var sql = new StringBuilder($"INSERT INTO {table} ({columns}) VALUES ");

            for (var id = start; id < Math.Min(start + 1_000, nodes + 1); id++)
            {
                if (id != start)
                {
                    sql.Append(',');
                }

                var depth = dense
                    ? id - 1
                    : forest
                        ? id - start
                        : id == 1
                            ? 0
                            : 1;

                var parent = dense
                    ? id == 1
                        ? "NULL"
                        : (id - 1).ToString(CultureInfo.InvariantCulture)
                    : forest
                        ? id == start
                            ? "NULL"
                            : (id - 1).ToString(CultureInfo.InvariantCulture)
                        : id == 1
                            ? "NULL"
                            : "1";

                var left = dense
                    ? id
                    : forest
                        ? depth + 1L
                        : id == 1
                            ? 1
                            : 2L * id - 2;

                var right = dense
                    ? 2L * nodes - depth
                    : forest
                        ? 2_000L - depth
                        : id == 1
                            ? 2L * nodes
                            : left + 1;

                var position = dense || forest
                    ? 0
                    : Math.Max(0, id - 2);

                if (branching)
                {
                    var offset = id == 1
                        ? 0
                        : (id - 2) % 1_000;

                    var branch = id == 1
                        ? 0
                        : (id - 2) / 1_000;

                    depth = id == 1
                        ? 0
                        : offset + 1;

                    parent = id == 1
                        ? "NULL"
                        : offset == 0
                            ? "1"
                            : (id - 1).ToString(CultureInfo.InvariantCulture);

                    left = id == 1
                        ? 1
                        : 2L + 2_000L * branch + offset;

                    right = id == 1
                        ? 200_002
                        : 2_001L + 2_000L * branch - offset;

                    position = offset == 0
                        ? branch
                        : 0;
                }

                if (balanced)
                {
                    depth = balancedCoordinates.Depths![id];
                    parent = id == 1
                        ? "NULL"
                        : (id / 2).ToString(CultureInfo.InvariantCulture);

                    left = balancedCoordinates.Lefts![id];
                    right = balancedCoordinates.Rights![id];
                    position = id == 1
                        ? 0
                        : id % 2;
                }

                sql.Append(
                    CultureInfo.InvariantCulture,
                    $"(@scope,{id},@tree,{parent},{left},{right},{depth},{position},1,'fixture')");
            }

            var scopeParameter = entity.FindProperty(nameof(CompositionNavigationNode.Scope))!
                .GetRelationalTypeMapping()
                .CreateParameter(parameterCommand, "scope", scope, nullable: false);

            var treeParameter = entity.FindProperty(nameof(CompositionNavigationNode.TreeId))!
                .GetRelationalTypeMapping()
                .CreateParameter(
                    parameterCommand,
                    "tree",
                    forest && start > 1
                        ? PrintableGuid()
                        : tree,
                    nullable: false);

            // WHY: Typed mappings retain the provider's GUID transport and only fixture cardinalities
            // enter literal SQL. Batches bound setup memory; the generated flag is never assigned.
            await context.Database.ExecuteSqlRawAsync(
                sql.ToString(),
                [scopeParameter, treeParameter],
                cancellationToken);
        }

        var analyze = "ANALYZE TABLE " + table;

        await context.Database.ExecuteSqlRawAsync(analyze, cancellationToken);

        return;

        string Column(
            string name
        ) => helper.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(store)!);
    }
}
