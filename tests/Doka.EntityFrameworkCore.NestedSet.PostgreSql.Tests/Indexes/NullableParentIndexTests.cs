using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Verifies native partial-index eligibility, ordinary plans, and real foreign-key checks.</summary>
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

    /// <summary>
    /// Partial indexes retain exact definitions and eligible access without requiring one cost choice.
    /// </summary>
    /// <param name="scoped">Whether the parent FK includes a scope column.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplainAsync_NullableParentIndexes_PreserveSelectiveAccessPaths(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var entity = Entity(context, scoped);
        var columns = Columns(context, entity, scoped);
        var indexes = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(entity.Name)!.GetIndexes();

        var dependent = indexes.Single(index => index.GetFilter() == columns.Parent + " IS NOT NULL");
        var roots = indexes.Single(index => index.GetFilter() == columns.Parent + " IS NULL");
        await SeedAsync(context, columns, scoped, cancellationToken: TestContext.Current.CancellationToken);
        var scope = scoped
            ? $"x.{columns.Scope} = @scope AND "
            : string.Empty;

        var predicate = scope + $"x.{columns.Key} = @key";
        var principalQuery = $"SELECT 1 FROM ONLY {columns.Table} x WHERE {predicate} FOR KEY SHARE OF x";
        var dependentQuery = $"SELECT 1 FROM ONLY {columns.Table} x WHERE {scope}x.{columns.Parent} = @key";
        var rootQuery = $"SELECT x.{columns.Key} FROM {columns.Table} x WHERE {scope}x.{columns.Parent} IS NULL";

        var nativeRoot = await ReadNativeIndexAsync(
            context,
            entity,
            roots.GetDatabaseName()!,
            TestContext.Current.CancellationToken);

        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var expectedColumns = roots.Properties.Select(property => property.GetColumnName(store)!).ToArray();

        // Act
        var principalPlan = await ExplainAsync(
            context,
            principalQuery,
            scoped,
            cancellationToken: TestContext.Current.CancellationToken);

        var dependentPlan = await ExplainAsync(
            context,
            dependentQuery,
            scoped,
            cancellationToken: TestContext.Current.CancellationToken);

        var rootPlan = await ExplainAsync(
            context,
            rootQuery,
            scoped,
            cancellationToken: TestContext.Current.CancellationToken);

        var rootKeys = await ReadRootKeysAsync(context, scoped, TestContext.Current.CancellationToken);

        await using var eligibility = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);

        // WHY: Reused-table statistics and heap visibility can legitimately select a sequential scan. This
        // transaction-local control tests eligibility only; the ordinary plans above remain separate evidence.
        await context.Database.ExecuteSqlRawAsync(
            "SET LOCAL enable_seqscan = off",
            TestContext.Current.CancellationToken);
        var principalEligibility = await ExplainAsync(
            context,
            principalQuery,
            scoped,
            TestContext.Current.CancellationToken);
        var dependentEligibility = await ExplainAsync(
            context,
            dependentQuery,
            scoped,
            TestContext.Current.CancellationToken);
        var rootEligibility = await ExplainAsync(
            context,
            rootQuery,
            scoped,
            TestContext.Current.CancellationToken);

        var eligibleRootKeys = await ReadRootKeysAsync(
            context,
            scoped,
            TestContext.Current.CancellationToken);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            "PostgreSql-nullable-parent-"
            + (scoped
                ? "scoped"
                : "unscoped"),
            [
                "ordinary principal\n" + principalPlan,
                "ordinary dependent\n" + dependentPlan,
                "ordinary roots\n" + rootPlan,
                "eligibility-only principal\n" + principalEligibility,
                "eligibility-only dependent\n" + dependentEligibility,
                "eligibility-only roots\n" + rootEligibility,
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(nativeRoot);
        Assert.Equal(expectedColumns, nativeRoot.Columns);
        Assert.Equal(roots.GetFilter()!.Trim('(', ')', ' '), nativeRoot.Predicate?.Trim('(', ')', ' '));
        Assert.True(nativeRoot.Valid);
        Assert.True(nativeRoot.Ready);
        Assert.False(nativeRoot.Unique);
        var principalIndexes = IndexNames(principalEligibility);
        var ordinaryPrincipalIndexes = IndexNames(principalPlan);
        Assert.NotEmpty(principalIndexes);
        Assert.All(
            indexes.Where(index => index.GetFilter() is not null),
            index =>
            {
                Assert.DoesNotContain(index.GetDatabaseName(), principalIndexes);
                Assert.DoesNotContain(index.GetDatabaseName(), ordinaryPrincipalIndexes);
            });

        Assert.Contains(dependent.GetDatabaseName(), IndexNames(dependentEligibility));
        Assert.Contains(roots.GetDatabaseName(), IndexNames(rootEligibility));
        Assert.Equal(
            scoped
                ? ["Tree", "NodeId", "Parent"]
                : ["Id", "ParentId"],
            roots.Properties.Select(property => property.Name));

        Assert.Equal(rootKeys.Order(), eligibleRootKeys.Order());
        Assert.Equal(Enumerable.Range(0, 1000).Select(index => (index * 20) + 1), rootKeys.Order());
        Assert.Equal(1000, rootKeys.Length);
        Assert.Contains(1, rootKeys);
        Assert.Contains(19981, rootKeys);
        Assert.Equal(1, ActualRows(principalPlan));
        Assert.Equal(19, ActualRows(dependentPlan));
        Assert.Equal(1000, ActualRows(rootPlan));
        Assert.Equal(1, ActualRows(principalEligibility));
        Assert.Equal(19, ActualRows(dependentEligibility));
        Assert.Equal(1000, ActualRows(rootEligibility));
    }

    /// <summary>Partial dependent indexes retain restrictive FK enforcement for roots with children.</summary>
    /// <param name="scoped">Whether the parent FK includes a scope column.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteDeleteAsync_NullableParentIndexes_RetainRestrictiveForeignKey(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var columns = Columns(context, Entity(context, scoped), scoped);
        await SeedAsync(context, columns, scoped, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var exception = await Record.ExceptionAsync(() => scoped
            ? context
                .Set<TreeNode>()
                .Where(node => node.Tree == 1 && node.NodeId == 1)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken)
            : context
                .Set<UnscopedQueryNode>()
                .IgnoreQueryFilters()
                .Where(node => node.Id == 1)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken));

        var count = scoped
            ? await context
                .Set<TreeNode>()
                .CountAsync(TestContext.Current.CancellationToken)
            : await context
                .Set<UnscopedQueryNode>()
                .IgnoreQueryFilters()
                .CountAsync(TestContext.Current.CancellationToken);

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
    ) => context.Model.FindEntityType(
        scoped
            ? typeof(TreeNode)
            : typeof(UnscopedQueryNode))!;

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
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="columns">The physical columns included in the statement.</param>
    /// <param name="scoped">Whether the hierarchy uses a scope.</param>
    private static async Task SeedAsync(
        DbContext context,
        ParentColumns columns,
        bool scoped,
        CancellationToken cancellationToken
    )
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
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

        await command.ExecuteNonQueryAsync(cancellationToken);
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
