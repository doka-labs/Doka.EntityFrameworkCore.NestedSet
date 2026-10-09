using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

/// <summary>Exercises optional SafeMigrations adapters against real generated nested-set migration code.</summary>
[DatabasePlatform]
public sealed partial class SafeMigrationTests : IClassFixture<MigrationFixture>
{
    private readonly MigrationFixture _fixture;

    /// <summary>Creates the suite with lazily shared server containers and isolated databases.</summary>
    /// <param name="fixture">The ordinary migration suite's provider-neutral container fixture.</param>
    public SafeMigrationTests(
        MigrationFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Creates an isolated database with explicitly enabled runtime and design-time adapters.</summary>
    private Task<MigrationDatabase> CreateDatabaseAsync(
        string engine,
        bool qualifySchema
    ) => _fixture.CreateDatabaseAsync(
        engine,
        options => SafeMigrationTestServices.ConfigureOptions(options, engine),
        SafeMigrationTestServices.ConfigureDesignServices,
        qualifySchema);

    /// <summary>Selects the index intents emitted by the compiled scaffolded migration.</summary>
    private static SafeMigrationOperation[] GetIndexOperations(
        MigrationChain chain,
        DbContext context,
        int migrationIndex = 0
    ) => GetUpOperations(chain, context, migrationIndex)
        .OfType<SafeMigrationOperation>()
        .Where(operation => operation.Intent is EnsureIndexIntent)
        .ToArray();

    /// <summary>Returns the complete ordered Up operation stream for one generated migration.</summary>
    private static MigrationOperation[] GetUpOperations(
        MigrationChain chain,
        DbContext context,
        int migrationIndex = 0
    ) => chain
        .GetMigration(context, migrationIndex)
        .UpOperations
        .ToArray();

    /// <summary>Asserts the exact fail-closed result for generated SQL check expressions.</summary>
    private static void AssertOpaqueCheckConstraintBlockers(
        SafeMigrationRunReport report
    )
    {
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        var blockers = report
            .Assessments
            .Where(assessment => assessment.Action == SafeMigrationAction.RejectUnsupported)
            .ToArray();

        Assert.Equal(4, blockers.Length);
        Assert.All(
            blockers,
            assessment =>
            {
                Assert.Equal(SafeMigrationOperationKind.EnsureCheckConstraint, assessment.OperationKind);
                Assert.Equal(SafeMigrationObservedState.Unsupported, assessment.ObservedState);
                Assert.Equal("opaque_sql_expression", assessment.AnalysisCode);
                Assert.Equal("unsupported", assessment.DecisionCode);
                Assert.False(assessment.PostconditionSatisfied);
            });
    }

    /// <summary>Asserts the exact current-model indexes using the actual physical catalog.</summary>
    private static void AssertCurrentIndexes(
        MigrationIndex[] indexes
    )
    {
        Assert.Equal(7, indexes.Length);
        Assert.Single(indexes, index => index.IsUnique && index.Columns.SequenceEqual(["tree_scope", "entry_id"]));
        Assert.All(indexes, index => Assert.True(index.IsOrdinary));
        Assert.Equal(
            [
                "entry_payload",
                "tree_scope,entry_id",
                "tree_scope,parent_entry",
                "tree_scope,tree_id,left_bound",
                "tree_scope,tree_id,parent_entry,entry_payload,entry_category,entry_id",
                "tree_scope,tree_id,parent_entry,sibling_position",
                "tree_scope,tree_id,right_bound",
            ],
            indexes
                .Select(index => string.Join(",", index.Columns))
                .Order(StringComparer.Ordinal));

        var order = Assert.Single(
            indexes,
            index => index.Columns.SequenceEqual(
            [
                "tree_scope",
                "tree_id",
                "parent_entry",
                "entry_payload",
                "entry_category",
                "entry_id",
            ]));

        Assert.Equal<bool>(
            [
                false,
                false,
                false,
                false,
                true,
                false
            ],
            order.Descending);
    }

    /// <summary>Asserts the exact historical index catalog restored by a generated Down migration.</summary>
    private static void AssertBaselineIndexes(
        MigrationIndex[] indexes
    )
    {
        Assert.Equal(5, indexes.Length);
        Assert.Single(indexes, index => index.IsUnique && index.Columns.SequenceEqual(["tree_scope", "entry_id"]));
        Assert.All(indexes, index => Assert.True(index.IsOrdinary));
        Assert.Equal(
            [
                "entry_payload",
                "tree_scope,entry_id",
                "tree_scope,left_bound",
                "tree_scope,parent_entry,sibling_position",
                "tree_scope,right_bound",
            ],
            indexes
                .Select(index => string.Join(",", index.Columns))
                .Order(StringComparer.Ordinal));
    }

    /// <summary>Asserts every index intent required by a fresh current-model migration.</summary>
    private static void AssertFreshIndexOperations(
        SafeMigrationOperation[] operations
    ) => AssertIndexOperations(
        operations,
        [
            "entry_payload",
            "tree_scope,parent_entry",
            "tree_scope,tree_id,left_bound",
            "tree_scope,tree_id,parent_entry,entry_payload,entry_category,entry_id",
            "tree_scope,tree_id,parent_entry,sibling_position",
            "tree_scope,tree_id,right_bound",
        ]);

    /// <summary>Asserts every index intent introduced by the current tree-identity upgrade.</summary>
    private static void AssertUpgradeIndexOperations(
        SafeMigrationOperation[] operations
    ) => AssertIndexOperations(
        operations,
        [
            "tree_scope,parent_entry",
            "tree_scope,tree_id,left_bound",
            "tree_scope,tree_id,parent_entry,entry_payload,entry_category,entry_id",
            "tree_scope,tree_id,parent_entry,sibling_position",
            "tree_scope,tree_id,right_bound",
        ]);

    /// <summary>Compares SafeMigrations intents by their complete ordered physical key columns.</summary>
    private static void AssertIndexOperations(
        SafeMigrationOperation[] operations,
        string[] expectedColumns
    )
    {
        var actualColumns = operations
            .Select(operation => string.Join(
                ",",
                ((EnsureIndexIntent)operation.Intent).Definition.Keys.Select(key => key.Column)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedColumns.Order(StringComparer.Ordinal), actualColumns);
    }

    /// <summary>Asserts checks, the scoped self-reference, and Int64 coordinate storage in the real catalog.</summary>
    private static void AssertCurrentPhysicalContract(
        string engine,
        MigrationIndex[] indexes,
        MigrationCheck[] checks,
        MigrationForeignKey[] foreignKeys,
        IReadOnlyDictionary<string, string> columnTypes
    )
    {
        AssertCurrentIndexes(indexes);
        Assert.Equal(4, checks.Length);
        Assert.Contains(
            checks,
            check => check.Name.Contains("LeftMin", StringComparison.Ordinal)
                && NormalizeSql(check.Expression)
                    .Contains("left_bound>=1", StringComparison.Ordinal));
        Assert.Contains(
            checks,
            check => check.Name.Contains("RightAfterLeft", StringComparison.Ordinal)
                && NormalizeSql(check.Expression)
                    .Contains("right_bound>left_bound", StringComparison.Ordinal));
        Assert.Contains(
            checks,
            check => check.Name.Contains("DepthMin", StringComparison.Ordinal)
                && NormalizeSql(check.Expression)
                    .Contains("entry_depth>=0", StringComparison.Ordinal));
        Assert.Contains(
            checks,
            check => check.Name.Contains("PositionMin", StringComparison.Ordinal)
                && NormalizeSql(check.Expression)
                    .Contains("sibling_position>=0", StringComparison.Ordinal));

        var parent = Assert.Single(foreignKeys);
        Assert.Equal(["tree_scope", "parent_entry"], parent.DependentColumns);
        Assert.Equal(["tree_scope", "entry_id"], parent.PrincipalColumns);
        Assert.True(
            parent
                .DeleteAction
                .Replace('_', ' ')
                .ToUpperInvariant() is "NO ACTION" or "RESTRICT");

        var expectedInteger = engine == "Sqlite" ? "integer" : "bigint";
        Assert.Equal(expectedInteger, columnTypes["left_bound"], ignoreCase: true);
        Assert.Equal(expectedInteger, columnTypes["right_bound"], ignoreCase: true);
        Assert.Equal(expectedInteger, columnTypes["sibling_position"], ignoreCase: true);
    }

    /// <summary>Normalizes provider quoting and whitespace for semantic check-expression assertions.</summary>
    private static string NormalizeSql(
        string expression
    ) => Regex.Replace(
        expression.ToLowerInvariant(),
        "[\\s\\\"`\\[\\]()]",
        "",
        RegexOptions.CultureInvariant);

    /// <summary>Captures names, order and uniqueness so replay and rejection must preserve the exact catalog.</summary>
    private static string[] IndexSignatures(
        MigrationIndex[] indexes
    ) => indexes
        .Select(index =>
            index.Name + ":" + index.IsUnique + ":" + index.IsOrdinary + ":" + string.Join(",", index.Columns))
        .Order(StringComparer.Ordinal)
        .ToArray();
}
