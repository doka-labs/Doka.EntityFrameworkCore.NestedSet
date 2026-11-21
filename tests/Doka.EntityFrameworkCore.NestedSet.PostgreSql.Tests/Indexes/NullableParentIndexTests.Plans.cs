using System.Collections.Generic;
using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

public sealed partial class NullableParentIndexTests
{

    /// <summary>Explains parameterized principal, dependent, or root reads with actual row counts.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="query">The composed provider query to execute.</param>
    /// <param name="scoped">Whether the hierarchy uses a scope.</param>
    private static async Task<string> ExplainAsync(
        DbContext context,
        string query,
        bool scoped,
        CancellationToken cancellationToken
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + query;
        var key = command.CreateParameter();
        key.ParameterName = "key";
        key.Value = 1;
        command.Parameters.Add(key);

        if (scoped)
        {
            var scope = command.CreateParameter();
            scope.ParameterName = "scope";
            scope.Value = 1;
            command.Parameters.Add(scope);
        }

        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Returns every index referenced by the actual plan, including nested lock wrappers.</summary>
    private static List<string> IndexNames(
        string plan
    )
    {
        using var document = JsonDocument.Parse(plan);
        var names = new List<string>();
        Collect(
            document
                .RootElement[0]
                .GetProperty("Plan"),
            names);

        return names;
    }

    /// <summary>Walks child plans while retaining only their physical index names.</summary>
    private static void Collect(
        JsonElement plan,
        List<string> names
    )
    {
        if (plan.TryGetProperty("Index Name", out var index))
        {
            names.Add(index.GetString()!);
        }

        if (plan.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                Collect(child, names);
            }
        }
    }

    /// <summary>Returns the top-level actual cardinality of one explain query.</summary>
    private static int ActualRows(
        string plan
    )
    {
        using var document = JsonDocument.Parse(plan);

        return document
            .RootElement[0]
            .GetProperty("Plan")
            .GetProperty("Actual Rows")
            .GetInt32();
    }
    /// <summary>Reads exact root identities independently of the active native access path.</summary>
    private static Task<int[]> ReadRootKeysAsync(
        DbContext context,
        bool scoped,
        CancellationToken cancellationToken
    ) => scoped
        ? context.Set<TreeNode>()
            .Where(node => node.Tree == 1 && node.Parent == null)
            .Select(node => node.NodeId)
            .ToArrayAsync(cancellationToken)
        : context.Set<UnscopedQueryNode>()
            .IgnoreQueryFilters()
            .Where(node => node.ParentId == null)
            .Select(node => node.Id)
            .ToArrayAsync(cancellationToken);
}
