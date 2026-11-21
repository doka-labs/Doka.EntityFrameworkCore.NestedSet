using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

public sealed partial class NullableParentIndexTests
{
    /// <summary>Missing or same-name wrong-filter indexes cannot satisfy the native root eligibility probe.</summary>
    /// <param name="scoped">Whether the physical hierarchy includes a scope.</param>
    /// <param name="wrongFilter">Whether a same-name index retains the keys but reverses the root predicate.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExplainAsync_MissingOrWrongRootIndex_DoesNotSatisfyEligibility(
        bool scoped,
        bool wrongFilter
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var entity = Entity(context, scoped);
        var columns = Columns(context, entity, scoped);
        var roots = context.GetService<IDesignTimeModel>().Model.FindEntityType(entity.Name)!
            .GetIndexes().Single(index => index.GetFilter() == columns.Parent + " IS NULL");

        await SeedAsync(context, columns, scoped, TestContext.Current.CancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);

        var sql = context.GetService<ISqlGenerationHelper>();
        var indexName = roots.GetDatabaseName()!;
        var drop = "DROP INDEX " + sql.DelimitIdentifier(indexName, entity.GetSchema());

        // WHY: DDL comes only from trusted fixture metadata. Transaction disposal restores the genuine index
        // after either adversarial variant, so no later case inherits a deliberately broken schema.
        await context.Database.ExecuteSqlRawAsync(drop, TestContext.Current.CancellationToken);

        if (wrongFilter)
        {
            var keys = scoped
                ? new[] { columns.Scope, columns.Key, columns.Parent }
                : new[] { columns.Key, columns.Parent };

            var create = "CREATE INDEX " + sql.DelimitIdentifier(indexName) + " ON " + columns.Table
                + " (" + string.Join(", ", keys) + ") WHERE " + columns.Parent + " IS NOT NULL";

            await context.Database.ExecuteSqlRawAsync(create, TestContext.Current.CancellationToken);
        }

        await context.Database.ExecuteSqlRawAsync(
            "SET LOCAL enable_seqscan = off",
            TestContext.Current.CancellationToken);
        var scope = scoped ? "x." + columns.Scope + " = @scope AND " : string.Empty;
        var query = $"SELECT x.{columns.Key} FROM {columns.Table} x WHERE {scope}x.{columns.Parent} IS NULL";

        // Act
        var native = await ReadNativeIndexAsync(context, entity, indexName, TestContext.Current.CancellationToken);
        var plan = await ExplainAsync(context, query, scoped, TestContext.Current.CancellationToken);
        var rootsAfterIndexChange = await ReadRootKeysAsync(context, scoped, TestContext.Current.CancellationToken);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            $"PostgreSql-nullable-parent-control-scoped-{scoped}-wrong-filter-{wrongFilter}",
            ["eligibility-only adversarial roots\n" + plan],
            TestContext.Current.CancellationToken);

        // Assert
        if (wrongFilter)
        {
            Assert.NotNull(native);
            Assert.Equal(columns.Parent + " IS NOT NULL", native.Predicate?.Trim('(', ')', ' '));
            Assert.True(native.Valid);
            Assert.True(native.Ready);
        }
        else
        {
            Assert.Null(native);
        }

        Assert.DoesNotContain(indexName, IndexNames(plan));
        Assert.Equal(1000, ActualRows(plan));
        Assert.Equal(
            Enumerable.Range(0, 1000).Select(index => (index * 20) + 1),
            rootsAfterIndexChange.Order());
    }

    /// <summary>Disposing the eligibility transaction restores the session's original planner choice policy.</summary>
    [Fact]
    public async Task DisposeAsync_EligibilityControl_RestoresSessionPlannerSetting()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var before = await ReadSequentialScanSettingAsync(context, TestContext.Current.CancellationToken);
        string during;

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(
                         TestContext.Current.CancellationToken))
        {
            await context.Database.ExecuteSqlRawAsync(
                "SET LOCAL enable_seqscan = off",
                TestContext.Current.CancellationToken);
            during = await ReadSequentialScanSettingAsync(context, TestContext.Current.CancellationToken);
        }

        var after = await ReadSequentialScanSettingAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("off", during);
        Assert.Equal(before, after);
    }

    /// <summary>Reads the current session setting on the connection retained across the probe transaction.</summary>
    private static async Task<string> ReadSequentialScanSettingAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 0;
        command.CommandText = "SHOW enable_seqscan";

        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
