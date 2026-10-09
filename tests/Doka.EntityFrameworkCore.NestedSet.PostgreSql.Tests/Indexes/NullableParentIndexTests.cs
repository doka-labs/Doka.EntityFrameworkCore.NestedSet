using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Verifies PostgreSQL partial parent indexes with fresh-table plans and real foreign-key checks.</summary>
public sealed partial class NullableParentIndexTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, PostgreSqlEngine>>
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the provider-owned database for independent nullable-parent probes.</summary>
    /// <param name="fixture">The isolated database fixture.</param>
    public NullableParentIndexTests(
        ProviderFixture<RelationalFixture, PostgreSqlEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Fresh statistics cannot make either partial index eligible for a principal-only lookup.</summary>
    /// <param name="scoped">Whether the parent FK includes a scope column.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullableParentIndexesPreserveSelectiveAccessPaths(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var entity = Entity(context, scoped);
        var columns = Columns(context, entity, scoped);
        var indexes = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(entity.Name)!.GetIndexes();

        var dependent = indexes.Single(index => index.GetFilter() == columns.Parent + " IS NOT NULL");
        var roots = indexes.Single(index => index.GetFilter() == columns.Parent + " IS NULL");
        await SeedAsync(context, columns, scoped);
        var scope = scoped ? $"x.{columns.Scope} = @scope AND " : string.Empty;
        var predicate = scope + $"x.{columns.Key} = @key";
        var principalQuery = $"SELECT 1 FROM ONLY {columns.Table} x WHERE {predicate} FOR KEY SHARE OF x";
        var dependentQuery = $"SELECT 1 FROM ONLY {columns.Table} x WHERE {scope}x.{columns.Parent} = @key";
        var rootQuery = $"SELECT x.{columns.Key} FROM {columns.Table} x WHERE {scope}x.{columns.Parent} IS NULL";

        // Act
        var principalPlan = await ExplainAsync(context, principalQuery, scoped);
        var dependentPlan = await ExplainAsync(context, dependentQuery, scoped);
        var rootPlan = await ExplainAsync(context, rootQuery, scoped);
        var rootKeys = scoped
            ? await context
                .Set<TreeNode>()
                .Where(node => node.Tree == 1 && node.Parent == null)
                .Select(node => node.NodeId)
                .ToArrayAsync(CancellationToken.None)
            : await context
                .Set<UnscopedQueryNode>()
                .IgnoreQueryFilters()
                .Where(node => node.ParentId == null)
                .Select(node => node.Id)
                .ToArrayAsync(CancellationToken.None);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            "PostgreSql-nullable-parent-" + (scoped ? "scoped" : "unscoped"),
            ["principal\n" + principalPlan, "dependent\n" + dependentPlan, "roots\n" + rootPlan]);

        // Assert
        var principalIndexes = IndexNames(principalPlan);
        Assert.NotEmpty(principalIndexes);
        Assert.All(
            indexes.Where(index => index.GetFilter() is not null),
            index => Assert.DoesNotContain(index.GetDatabaseName(), principalIndexes));
        Assert.Contains(dependent.GetDatabaseName(), IndexNames(dependentPlan));
        Assert.Contains(roots.GetDatabaseName(), IndexNames(rootPlan));
        Assert.Equal(
            scoped ? ["Tree", "NodeId", "Parent"] : ["Id", "ParentId"],
            roots.Properties.Select(property => property.Name));
        Assert.Equal(1000, rootKeys.Length);
        Assert.Contains(1, rootKeys);
        Assert.Contains(19981, rootKeys);
        Assert.Equal(1, ActualRows(principalPlan));
        Assert.Equal(19, ActualRows(dependentPlan));
        Assert.Equal(1000, ActualRows(rootPlan));
    }

    /// <summary>Partial dependent indexes retain restrictive FK enforcement for roots with children.</summary>
    /// <param name="scoped">Whether the parent FK includes a scope column.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullableParentIndexesRetainRestrictiveForeignKey(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var columns = Columns(context, Entity(context, scoped), scoped);
        await SeedAsync(context, columns, scoped);

        // Act
        var exception = await Record.ExceptionAsync(() => scoped
            ? context
                .Set<TreeNode>()
                .Where(node => node.Tree == 1 && node.NodeId == 1)
                .ExecuteDeleteAsync(CancellationToken.None)
            : context
                .Set<UnscopedQueryNode>()
                .IgnoreQueryFilters()
                .Where(node => node.Id == 1)
                .ExecuteDeleteAsync(CancellationToken.None));

        var count = scoped
            ? await context
                .Set<TreeNode>()
                .CountAsync(CancellationToken.None)
            : await context
                .Set<UnscopedQueryNode>()
                .IgnoreQueryFilters()
                .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            PostgresErrorCodes.ForeignKeyViolation,
            Assert.IsType<PostgresException>(exception)
                .SqlState);
        Assert.Equal(20000, count);
    }

    /// <summary>Gets one of the fixture's existing scoped or unscoped hierarchy types.</summary>
    private static IEntityType Entity(
        DbContext context,
        bool scoped
    ) => context.Model.FindEntityType(scoped ? typeof(TreeNode) : typeof(UnscopedQueryNode))!;

    /// <summary>Uses actual physical columns so plans exercise the same metadata as generated migration SQL.</summary>
    private static ParentColumns Columns(
        DbContext context,
        IEntityType entity,
        bool scoped
    )
    {
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var names = scoped
            ? new[]
            {
                "NodeId",
                "TreeId",
                "Parent",
                "Start",
                "End",
                "Depth",
                "Position",
                "Tree",
            }
            :
            [
                "Id",
                "TreeId",
                "ParentId",
                "Left",
                "Right",
                "Depth",
                "Position",
                "Visible",
            ];

        var columns = names
            .Select(name => sql.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(store)!))
            .ToArray();

        return new ParentColumns(sql.DelimitIdentifier(store.Name, store.Schema), columns);
    }

    /// <summary>Seeds valid twenty-node trees without updating statistics or overriding planner settings.</summary>
    private static async Task SeedAsync(
        DbContext context,
        ParentColumns columns,
        bool scoped
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = $"""
                               INSERT INTO {columns.Table} ({string.Join(", ", columns.Values)})
                               SELECT n, md5(((n - 1) / 20)::text)::uuid,
                                   CASE WHEN (n - 1) % 20 = 0 THEN NULL ELSE ((n - 1) / 20) * 20 + 1 END,
                                   CASE WHEN (n - 1) % 20 = 0 THEN 1 ELSE ((n - 1) % 20) * 2 END,
                                   CASE WHEN (n - 1) % 20 = 0 THEN 40 ELSE ((n - 1) % 20) * 2 + 1 END,
                                   CASE WHEN (n - 1) % 20 = 0 THEN 0 ELSE 1 END,
                                   CASE WHEN (n - 1) % 20 = 0 THEN 0 ELSE ((n - 1) % 20) - 1 END,
                                   {(scoped ? "1" : "true")}
                               FROM generate_series(1, 20000) AS seed(n)
                               """;

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>Explains parameterized principal, dependent, or root reads with actual row counts.</summary>
    private static async Task<string> ExplainAsync(
        DbContext context,
        string query,
        bool scoped
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

        return (string)(await command.ExecuteScalarAsync(CancellationToken.None))!;
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

    /// <summary>Retains the fixture's delimited table and structural columns in insert order.</summary>
    private sealed record ParentColumns(
        string Table,
        string[] Values
    )
    {
        /// <summary>Gets the physical node-key expression.</summary>
        internal string Key => Values[0];

        /// <summary>Gets the physical nullable parent expression.</summary>
        internal string Parent => Values[2];

        /// <summary>Gets the physical scope expression when the hierarchy is scoped.</summary>
        internal string Scope => Values[7];
    }
}
