using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

public sealed partial class NullableParentIndexTests
{
    /// <summary>Generic Parent equality proves the dependent partial predicate without substituted constants.</summary>
    /// <param name="scoped">Whether the prepared parent identity includes Scope.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericParentPlansUseDependentPartialIndex(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var columns = Columns(context, Entity(context, scoped), scoped);
        var dependent = DesignIndexes(context, scoped)
            .Single(index => index.GetFilter() == columns.Parent + " IS NOT NULL");

        await SeedAsync(context, columns, scoped);
        var name = PreparedName();
        var scope = scoped ? $"x.{columns.Scope} = $1 AND " : string.Empty;
        var parent = scoped ? "$2" : "$1";
        var query = $"SELECT 1 FROM ONLY {columns.Table} x WHERE {scope}x.{columns.Parent} = {parent}";

        // Act
        var result = await ExplainGenericAsync(context, name, query, scoped, "1", CancellationToken.None);

        // Assert
        AssertGenericPlan(result, 19);
        Assert.Contains(dependent.GetDatabaseName(), IndexNames(result.Plan));
    }

    /// <summary>A generic root read retains its Parent-null companion and parameterized NodeKey prefix.</summary>
    /// <param name="scoped">Whether the prepared root read includes Scope.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericRootPlansUseRootPartialIndex(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var columns = Columns(context, Entity(context, scoped), scoped);
        var roots = DesignIndexes(context, scoped)
            .Single(index => index.GetFilter() == columns.Parent + " IS NULL");

        await SeedAsync(context, columns, scoped);
        var name = PreparedName();
        var scope = scoped ? $"x.{columns.Scope} = $1 AND " : string.Empty;
        var key = scoped ? "$2" : "$1";
        var query = $"SELECT x.{columns.Key} FROM {columns.Table} x "
            + $"WHERE {scope}x.{columns.Key} >= {key} AND x.{columns.Parent} IS NULL";

        // Act
        var result = await ExplainGenericAsync(context, name, query, scoped, "1", CancellationToken.None);

        // Assert
        AssertGenericPlan(result, 1000);
        Assert.Contains(roots.GetDatabaseName(), IndexNames(result.Plan));
    }

    /// <summary>A generic principal-only check cannot prove any generated partial-index predicate.</summary>
    /// <param name="scoped">Whether the prepared principal identity includes Scope.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericPrincipalPlansExcludePartialIndexes(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var columns = Columns(context, Entity(context, scoped), scoped);
        var indexes = DesignIndexes(context, scoped);
        await SeedAsync(context, columns, scoped);
        var name = PreparedName();
        var scope = scoped ? $"x.{columns.Scope} = $1 AND " : string.Empty;
        var key = scoped ? "$2" : "$1";
        var query = $"SELECT 1 FROM ONLY {columns.Table} x "
            + $"WHERE {scope}x.{columns.Key} = {key} FOR KEY SHARE OF x";

        // Act
        var result = await ExplainGenericAsync(context, name, query, scoped, "1", CancellationToken.None);

        // Assert
        AssertGenericPlan(result, 1);
        var used = IndexNames(result.Plan);
        Assert.NotEmpty(used);
        Assert.All(
            indexes.Where(index => index.GetFilter() is not null),
            index => Assert.DoesNotContain(index.GetDatabaseName(), used));
    }

    /// <summary>An execution error clears the aborted probe transaction before deallocating its statement.</summary>
    [Fact]
    public async Task GenericPlanSqlFailureRestoresSession()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var name = PreparedName();
        var before = await PreparedSessionAsync(context, name);

        // Act
        var exception = await Record.ExceptionAsync(() => ExplainGenericAsync(
            context,
            name,
            "SELECT 1 / $1",
            false,
            "0",
            CancellationToken.None));

        // Assert
        Assert.Equal(
            PostgresErrorCodes.DivisionByZero,
            Assert.IsType<PostgresException>(exception)
                .SqlState);

        var after = await PreparedSessionAsync(context, name);
        Assert.Equal(before, after);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            "PostgreSql-generic-error-" + name,
            [
                "sqlstate=" + PostgresErrorCodes.DivisionByZero,
                $"mode_before={before.Mode}\nmode_after={after.Mode}\nowned_statement_count={after.Count}",
            ]);
    }

    /// <summary>Canceled execution restores a nondefault session mode without removing other statements.</summary>
    [Fact]
    public async Task GenericPlanCancellationRestoresSession()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var name = PreparedName();
        var retained = PreparedName();
        var original = await PreparedSessionAsync(context, name);
        await PreparedControlAsync(context, $"PREPARE {retained} AS SELECT 1");
        await SetSessionModeAsync(context, "force_custom_plan");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        try
        {
            // Act
            var exception = await Record.ExceptionAsync(() => ExplainGenericAsync(
                context,
                name,
                "SELECT $1",
                false,
                "1",
                cancellation.Token));

            // Assert
            Assert.IsType<OperationCanceledException>(exception, exactMatch: false);

            var after = await PreparedSessionAsync(context, name);
            Assert.Equal("force_custom_plan", after.Mode);
            Assert.Equal(0, after.Count);

            var other = await PreparedSessionAsync(context, retained);
            Assert.Equal(1, other.Count);
            await QueryPlanTestSupport.WriteEvidenceAsync(
                "PostgreSql-generic-cancel-" + name,
                [
                    "query_cancelled=True",
                    $"mode_after={after.Mode}\nowned_statement_count={after.Count}\nretained_statement_count={other.Count}",
                ]);
        }
        finally
        {
            try
            {
                await PreparedControlAsync(context, $"DEALLOCATE {retained}");
            }
            finally
            {
                await SetSessionModeAsync(context, original.Mode);
            }
        }
    }

    /// <summary>Reads design-time filters that identify the actual generated physical index names.</summary>
    private static IIndex[] DesignIndexes(
        DbContext context,
        bool scoped
    ) => context
        .GetService<IDesignTimeModel>()
        .Model
        .FindEntityType(Entity(context, scoped).Name)!
        .GetIndexes()
        .ToArray();

    /// <summary>Verifies actual generic planning, cardinality, exact setting restoration, and owned cleanup.</summary>
    private static void AssertGenericPlan(
        GenericPlanResult result,
        int rows
    )
    {
        Assert.Equal(1, result.GenericPlans);
        Assert.Equal(0, result.CustomPlans);
        Assert.True(result.FromSql);
        Assert.Contains("$1", result.Plan, StringComparison.Ordinal);
        Assert.Equal(rows, ActualRows(result.Plan));
        Assert.Equal(result.PreviousMode, result.After.Mode);
        Assert.Equal(0, result.After.Count);
    }
}
