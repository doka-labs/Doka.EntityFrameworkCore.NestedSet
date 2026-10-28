using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerAncestorBoundsTests
{
    /// <summary>Provides both optional-scope layouts and failures in each distinct partition region.</summary>
    public static TheoryData<bool, string, int, int> RelationalPathCases
    {
        get
        {
            var result = new TheoryData<bool, string, int, int>();

            foreach (var scoped in new[] { true, false })
            {
                result.Add(scoped, "valid", 0, 2);
                result.Add(scoped, "hidden-above", 0, 2);
                result.Add(scoped, "off-path", 0, 2);
                result.Add(scoped, "same-block", 0, 2);
                result.Add(scoped, "same-block-blocked", 6, 0);
                result.Add(scoped, "empty", 0, 0);
                result.Add(scoped, "row-aware-valid", 0, 2);
                result.Add(scoped, "row-aware-blocked", 6, 0);

                foreach (var location in new[] { 2, 6, 11 })
                {
                    result.Add(scoped, "blocked", location, 0);
                    result.Add(scoped, "hidden", location, 0);
                    result.Add(scoped, "missing", location, 0);
                    result.Add(scoped, "depth", location, 0);
                    result.Add(scoped, "cross-tree", location, 0);
                }
            }

            result.Add(true, "cross-scope", 6, 0);

            return result;
        }
    }

    /// <summary>The exact new helper preserves path failures, native converted identity and caller bags.</summary>
    /// <param name="scoped">Whether the model has an explicit scope role.</param>
    /// <param name="mode">The independent structural, visibility or edge-rule adversary.</param>
    /// <param name="location">The failed child in the upper fringe, interior or lower fringe.</param>
    /// <param name="expectedCount">The independently known accepted occurrence count.</param>
    [Theory]
    [MemberData(nameof(RelationalPathCases))]
    public async Task RelationalPathSummariesExecuteExactFailureSemantics(
        bool scoped,
        string mode,
        int location,
        int expectedCount
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CoordinateBoundsContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets());

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CoordinateBoundsContext(options.UseInternalServiceProvider(services).Options, scoped);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);

        if (mode is "missing" or "off-path" or "cross-scope")
        {
            // WHY: These isolated fixtures intentionally reproduce legacy FK corruption. The ordinary
            // schema correctly rejects that seed, so only this case-owned connection disables FK checks.
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF", CancellationToken.None);
        }

        var nodes = Enumerable.Range(0, 13).Select(index => BoundsNode(index, index + 1, 26 - index,
            parent: index == 0 ? null : index - 1, depth: index)).ToList();

        switch (mode)
        {
            case "blocked": nodes[location].Position = 99; break;
            case "row-aware-blocked": nodes[location].Position = 99; break;
            case "hidden": nodes[location].Visible = false; break;
            case "missing": nodes[location].ParentId = new BoundsKey(999); break;
            case "depth": nodes[location].Depth = 50; break;
            case "hidden-above": nodes[0].Visible = false; break;
            case "empty":
                foreach (var node in nodes)
                {
                    node.Visible = false;
                }

                break;
            case "cross-tree":
                nodes[location].ParentId = new BoundsKey(13);
                nodes.Add(BoundsNode(13, 1, 26, depth: location - 1, tree: "b"));
                break;
            case "cross-scope":
                nodes[location].ParentId = new BoundsKey(13);
                nodes.Add(BoundsNode(13, 1, 26, depth: location - 1, scope: 8));
                break;
            case "off-path": nodes.Add(BoundsNode(13, 30, 31, parent: 999, depth: 1)); break;
            case "same-block":
            case "same-block-blocked":
                nodes.Add(BoundsNode(13, 10_001, 10_002, depth: 1));

                if (mode == "same-block-blocked")
                {
                    nodes[location].Position = 99;
                }

                break;
        }


        await context.AddRangeAsync(nodes, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var root = new NestedSetQuery<CoordinateBoundsNode>(context,
            context.Model.FindEntityType(typeof(CoordinateBoundsNode))!);

        if (scoped)
        {
            root = root.ForScope(7);
        }

        var visible = context.Set<CoordinateBoundsNode>().AsNoTracking().Where(node => node.TreeId == "a");

        if (scoped)
        {
            visible = visible.Where(node => node.Scope == 7);
        }

        var upperDepth = mode == "hidden-above" ? 1 : 0;
        var rows = visible.Where(node => node.Depth == upperDepth)
            .SelectMany(_ => visible.Where(node => node.Depth == 12), (upper, lower) => new { Upper = upper, Lower = lower });

        Expression<Func<object, CoordinateBoundsNode, CoordinateBoundsNode, bool>> allowed =
            (_, parent, child) => child.Position != 99;

        var occurrences = rows.Concat(rows);
        var filtered = mode.StartsWith("row-aware", StringComparison.Ordinal)
            ? root.WherePathEdges(occurrences, row => row.Upper, row => row.Lower,
                (row, parent, child) => child.Position != row.Upper.Position + 99, normalized: true)
            : FilterRelationalFailures(root, occurrences, row => row.Upper, row => row.Lower, allowed);

        // Act
        var actual = await filtered.Select(row => row.Lower.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedCount, actual.Length);
        Assert.All(actual, key => Assert.Equal(12, key.Value));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Executes actual production classification and grouping without altering SQLite capabilities.</summary>
    private static IQueryable<TRow> FilterRelationalFailures<TRow>(
        NestedSetQuery<CoordinateBoundsNode> root,
        IQueryable<TRow> rows,
        Expression<Func<TRow, CoordinateBoundsNode>> upper,
        Expression<Func<TRow, CoordinateBoundsNode>> lower,
        LambdaExpression allowed
    )
    {
        // WHY: This host cannot run SQL Server. Reflection selects these exact private production
        // expressions for relational semantic execution while the selected SQLite adapter stays unchanged.
        var type = root.GetType();
        var nodes = (IQueryable<CoordinateBoundsNode>)type.GetField("_nodes", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(root)!;

        var key = nodes.ElementType;
        var entity = ((DbContext)type.GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(root)!)
            .Model.FindEntityType(key)!;

        var projection = (Expression<Func<CoordinateBoundsNode, CoordinateBoundsNode, NestedSetQueryEdge<CoordinateBoundsNode>>>)
            type.GetField("s_edgeProjection", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        var edges = NestedSetParentExpressions<CoordinateBoundsNode, NestedSetQueryEdge<CoordinateBoundsNode>>.Join(
            nodes, entity.FindProperty(nameof(CoordinateBoundsNode.Id))!, entity.FindProperty(nameof(CoordinateBoundsNode.ParentId))!,
            null, projection);

        var failures = (IQueryable<CoordinateBoundsNode>)type
            .GetMethod("InvalidPathChildSource", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(root, [allowed, edges])!;

        var row = Expression.Parameter(typeof(TRow), "row");
        var violation = (Expression)type.GetMethod("InvalidRelationalPathChildren", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(root, [NestedSetQueryExpressions.Inline(upper, row), NestedSetQueryExpressions.Inline(lower, row), failures])!;

        return rows.Where(Expression.Lambda<Func<TRow, bool>>(Expression.Not(violation), row));
    }
}
