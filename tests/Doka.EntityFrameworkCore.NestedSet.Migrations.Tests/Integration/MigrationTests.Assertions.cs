namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class MigrationTests
{
    /// <summary>Checks application and structural indexes against their complete expected physical key order.</summary>
    private static void AssertCurrentIndexes(
        string engine,
        MigrationIndex[] indexes
    )
    {
        Assert.Equal(
            engine == "PostgreSql"
                ? 8
                : 7,
            indexes.Length);

        Assert.Single(indexes, x => x.IsUnique && x.Columns.SequenceEqual(["tree_scope", "entry_id"]));
        Assert.All(
            indexes,
            index => Assert.Equal(
                engine != "PostgreSql" || index.IsUnique || index.Name == "application_payload_lookup",
                index.IsOrdinary));

        var columns = indexes
            .Select(x => string.Join(",", x.Columns))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] expected =
        [
            "entry_payload",
            "tree_scope,entry_id",
            "tree_scope,parent_entry",
            "tree_scope,tree_id,left_bound",
            "tree_scope,tree_id,parent_entry,entry_payload,entry_category,entry_id",
            "tree_scope,tree_id,parent_entry,sibling_position",
            "tree_scope,tree_id,right_bound",
        ];

        Assert.Equal(
            engine == "PostgreSql"
                ? expected
                    .Append("tree_scope,entry_id,parent_entry")
                    .Order(StringComparer.Ordinal)
                : expected,
            columns);
    }

    /// <summary>Checks the real provider catalog against the complete physical hierarchy contract.</summary>
    private static void AssertPhysicalContract(
        string engine,
        MigrationIndex[] indexes,
        MigrationCheck[] checks,
        MigrationForeignKey[] foreignKeys,
        IReadOnlyDictionary<string, string> columnTypes
    )
    {
        AssertCurrentIndexes(engine, indexes);
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
                false,
            ],
            order.Descending);

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

        var expectedInteger = engine == "Sqlite"
            ? "integer"
            : "bigint";

        Assert.Equal(expectedInteger, columnTypes["left_bound"], ignoreCase: true);
        Assert.Equal(expectedInteger, columnTypes["right_bound"], ignoreCase: true);
        Assert.Equal(expectedInteger, columnTypes["sibling_position"], ignoreCase: true);
    }

    /// <summary>Normalizes provider quoting and whitespace for semantic check-expression assertions.</summary>
    private static string NormalizeSql(
        string expression
    ) => NormalizeSqlRegex()
        .Replace(expression.ToLowerInvariant(), "");

    /// <summary>Compares catalog definitions by value, including physical names and provider index facets.</summary>
    private static string[] IndexSignatures(
        IEnumerable<MigrationIndex> indexes
    ) => indexes
        .Select(x => $"{x.Name}:{string.Join(",", x.Columns)}:{string.Join(",", x.Descending)}:"
            + $"{x.IsUnique}:{x.IsOrdinary}")
        .ToArray();

    [GeneratedRegex("[\\s\\\"`\\[\\]()]", RegexOptions.CultureInvariant)]
    private static partial Regex NormalizeSqlRegex();
}
