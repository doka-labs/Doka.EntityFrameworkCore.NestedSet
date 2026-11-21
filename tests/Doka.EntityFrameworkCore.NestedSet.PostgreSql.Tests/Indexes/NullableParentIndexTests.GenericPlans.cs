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
    public async Task ExplainGenericAsync_ParentPlans_UseDependentPartialIndex(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var columns = Columns(context, Entity(context, scoped), scoped);
        var dependent = DesignIndexes(context, scoped)
            .Single(index => index.GetFilter() == columns.Parent + " IS NOT NULL");

        await SeedAsync(context, columns, scoped, cancellationToken: TestContext.Current.CancellationToken);
        var name = PreparedName();
        var scope = scoped
            ? $"x.{columns.Scope} = $1 AND "
            : string.Empty;

        var parent = scoped
            ? "$2"
            : "$1";

        var query = $"SELECT 1 FROM ONLY {columns.Table} x WHERE {scope}x.{columns.Parent} = {parent}";

        // Act
        var ordinary = await ExplainGenericAsync(
            context, name, query, scoped, "1", false, TestContext.Current.CancellationToken);
        var result = await ExplainGenericAsync(
            context, PreparedName(), query, scoped, "1", true, TestContext.Current.CancellationToken);

        // Assert
        AssertGenericPlan(ordinary, 19);
        AssertGenericPlan(result, 19);
        Assert.Contains(dependent.GetDatabaseName(), IndexNames(result.Plan));
    }

    /// <summary>A generic root read retains its Parent-null companion and parameterized NodeKey prefix.</summary>
    /// <param name="scoped">Whether the prepared root read includes Scope.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplainGenericAsync_RootPlans_UseRootPartialIndex(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var columns = Columns(context, Entity(context, scoped), scoped);
        var roots = DesignIndexes(context, scoped)
            .Single(index => index.GetFilter() == columns.Parent + " IS NULL");

        await SeedAsync(context, columns, scoped, cancellationToken: TestContext.Current.CancellationToken);
        var name = PreparedName();
        var scope = scoped
            ? $"x.{columns.Scope} = $1 AND "
            : string.Empty;

        var key = scoped
            ? "$2"
            : "$1";

        var query = $"SELECT x.{columns.Key} FROM {columns.Table} x "
        + $"WHERE {scope}x.{columns.Key} >= {key} AND x.{columns.Parent} IS NULL";

        // Act
        var ordinary = await ExplainGenericAsync(
            context, name, query, scoped, "1", false, TestContext.Current.CancellationToken);
        var result = await ExplainGenericAsync(
            context, PreparedName(), query, scoped, "1", true, TestContext.Current.CancellationToken);

        // Assert
        AssertGenericPlan(ordinary, 1000);
        AssertGenericPlan(result, 1000);
        Assert.Contains(roots.GetDatabaseName(), IndexNames(result.Plan));
    }

    /// <summary>A generic principal-only check cannot prove any generated partial-index predicate.</summary>
    /// <param name="scoped">Whether the prepared principal identity includes Scope.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplainGenericAsync_PrincipalPlans_ExcludePartialIndexes(
        bool scoped
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var columns = Columns(context, Entity(context, scoped), scoped);
        var indexes = DesignIndexes(context, scoped);
        await SeedAsync(context, columns, scoped, cancellationToken: TestContext.Current.CancellationToken);
        var name = PreparedName();
        var scope = scoped
            ? $"x.{columns.Scope} = $1 AND "
            : string.Empty;

        var key = scoped
            ? "$2"
            : "$1";

        var query = $"SELECT 1 FROM ONLY {columns.Table} x "
        + $"WHERE {scope}x.{columns.Key} = {key} FOR KEY SHARE OF x";

        // Act
        var ordinary = await ExplainGenericAsync(
            context, name, query, scoped, "1", false, TestContext.Current.CancellationToken);
        var result = await ExplainGenericAsync(
            context, PreparedName(), query, scoped, "1", true, TestContext.Current.CancellationToken);

        // Assert
        AssertGenericPlan(ordinary, 1);
        AssertGenericPlan(result, 1);
        var used = IndexNames(result.Plan);
        var ordinaryUsed = IndexNames(ordinary.Plan);
        Assert.NotEmpty(used);
        Assert.All(
            indexes.Where(index => index.GetFilter() is not null),
            index =>
            {
                Assert.DoesNotContain(index.GetDatabaseName(), used);
                Assert.DoesNotContain(index.GetDatabaseName(), ordinaryUsed);
            });
    }

    /// <summary>An execution error clears the aborted probe transaction before deallocating its statement.</summary>
    [Fact]
    public async Task ExplainGenericAsync_GenericPlanSqlFailure_RestoresSession()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var name = PreparedName();
        var before = await PreparedSessionAsync(
            context,
            name,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var exception = await Record.ExceptionAsync(() => ExplainGenericAsync(
            context,
            name,
            "SELECT 1 / $1",
            false,
            "0",
            false,
            TestContext.Current.CancellationToken));

        var after = await PreparedSessionAsync(context, name, cancellationToken: TestContext.Current.CancellationToken);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            "PostgreSql-generic-error-" + name,
            [
                "sqlstate=" + PostgresErrorCodes.DivisionByZero,
                $"mode_before={before.Mode}\nmode_after={after.Mode}\nowned_statement_count={after.Count}",
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            PostgresErrorCodes.DivisionByZero,
            Assert.IsType<PostgresException>(exception)
                .SqlState);

        Assert.Equal(before, after);
    }

    /// <summary>Canceled execution restores a nondefault session mode without removing other statements.</summary>
    [Fact]
    public async Task ExplainGenericAsync_GenericPlanCancellation_RestoresSession()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var name = PreparedName();
        var retained = PreparedName();
        var original = await PreparedSessionAsync(
            context,
            name,
            cancellationToken: TestContext.Current.CancellationToken);

        await PreparedControlAsync(
            context,
            $"PREPARE {retained} AS SELECT 1",
            cancellationToken: TestContext.Current.CancellationToken);

        await SetSessionModeAsync(
            context,
            "force_custom_plan",
            cancellationToken: TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Exception? operationFailure = null;

        try
        {
            // Act
            var exception = await Record.ExceptionAsync(() => ExplainGenericAsync(
                context,
                name,
                "SELECT $1",
                false,
                "1",
                true,
                cancellation.Token));

            var after = await PreparedSessionAsync(
                context,
                name,
                cancellationToken: TestContext.Current.CancellationToken);

            var other = await PreparedSessionAsync(
                context,
                retained,
                cancellationToken: TestContext.Current.CancellationToken);

            await QueryPlanTestSupport.WriteEvidenceAsync(
                "PostgreSql-generic-cancel-" + name,
                [
                    "query_cancelled=True",
                    $"mode_after={after.Mode}\nowned_statement_count={
                        after.Count}\nretained_statement_count={other.Count}",
                ],
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<OperationCanceledException>(exception, exactMatch: false);
            Assert.Equal("force_custom_plan", after.Mode);
            Assert.Equal(0, after.Count);
            Assert.Equal(1, other.Count);
        }
        catch (Exception failure)
        {
            operationFailure = failure;
        }
        finally
        {
            await CleanupPreparedSessionAsync(context, retained, original.Mode, operationFailure);
        }
    }

    /// <summary>A statement name owned by the caller cannot be acquired, changed, or removed by the probe.</summary>
    [Fact]
    public async Task ExplainGenericAsync_ExistingStatement_PreservesForeignSessionState()
    {
        // Arrange
        var runnerToken = TestContext.Current.CancellationToken;
        var database = await _fixture.ResetAsync(Engine, runnerToken);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync(runnerToken);
        var name = PreparedName();
        await PreparedControlAsync(context, $"PREPARE {name} AS SELECT 17", runnerToken);
        var before = await PreparedSessionAsync(context, name, runnerToken);
        Exception? ownershipFailure = null;

        try
        {
            // Act
            var exception = await Record.ExceptionAsync(() => ExplainGenericAsync(
                context, name, "SELECT $1", false, "99", false, runnerToken));
            var after = await PreparedSessionAsync(context, name, runnerToken);
            int? retainedValue = null;

            if (after.Count != 0)
            {
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"EXECUTE {name}";
                retainedValue = (int)(await command.ExecuteScalarAsync(runnerToken))!;
            }

            // Assert
            Assert.Equal(before, after);
            Assert.Equal(17, retainedValue);
            Assert.IsType<InvalidOperationException>(exception);
        }
        catch (Exception failure)
        {
            ownershipFailure = failure;
        }
        finally
        {
            await CleanupPreparedSessionAsync(context, name, before.Mode, ownershipFailure);
        }
    }

    /// <summary>Reads design-time filters that identify the actual generated physical index names.</summary>
    private static IIndex[] DesignIndexes(
        DbContext context,
        bool scoped
    ) => context
        .GetService<IDesignTimeModel>()
        .Model
        .FindEntityType(
            Entity(context, scoped)
                .Name)!
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
        Assert.Equal(result.Before, result.After);
        Assert.Equal(0, result.After.Count);
    }
}
