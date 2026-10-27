namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks the narrow fixture reader independently of optimizer choices and emitted aliases.</summary>
public sealed class AncestorIndexHydrationJoinTests
{
    /// <summary>Resolves direct and filtered payloads and detects the wrong directed edge despite the same join count.</summary>
    /// <param name="derived">Whether each filtered payload has its own derived SELECT.</param>
    /// <param name="swapped">Whether source hydration is incorrectly directed instead of owner hydration.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReadsImmediatePayloadJoinKinds(bool derived, bool swapped)
    {
        // Arrange
        var sql = FixtureSql(derived, swapped);

        // Act
        var actual = AncestorIndexHydrationJoins.Read(sql, "FixtureNodes");

        // Assert
        Assert.Equal(2, sql.Split("STRAIGHT_JOIN", StringSplitOptions.None).Length - 1);
        Assert.Equal(swapped ? "STRAIGHT_JOIN" : "INNER JOIN", actual.Source);
        Assert.Equal(swapped ? "INNER JOIN" : "STRAIGHT_JOIN", actual.Owner);
        Assert.Equal(!swapped, actual == ("INNER JOIN", "STRAIGHT_JOIN"));
    }

    /// <summary>Missing, ambiguous and unsupported fixture syntax fails attribution rather than passing a loose join match.</summary>
    /// <param name="shape">The independently malformed SQL source or alias.</param>
    [Theory]
    [InlineData("wrong-table")]
    [InlineData("missing-alias")]
    [InlineData("reused-alias")]
    [InlineData("unbalanced")]
    [InlineData("literal")]
    [InlineData("unsupported-edge")]
    public void InvalidPayloadAttributionFailsClosed(string shape)
    {
        // Arrange
        var valid = FixtureSql(derived: true, swapped: false);
        var sql = shape switch
        {
            "wrong-table" => valid.Replace("`FixtureNodes`", "`OtherNodes`", StringComparison.Ordinal),
            "missing-alias" => valid.Replace("AS `owner` ON", "AS `otherOwner` ON", StringComparison.Ordinal),
            "reused-alias" => valid.Replace("FROM `modes` AS `m`",
                "FROM `modes` AS `m` INNER JOIN `FixtureNodes` AS `owner` ON 1 = 1", StringComparison.Ordinal),
            "unbalanced" => valid.Replace("STRAIGHT_JOIN (", "STRAIGHT_JOIN ", StringComparison.Ordinal),
            "literal" => valid.Replace("`n`.`Visible`", "`n`.`Label` = 'not a ) source'", StringComparison.Ordinal),
            "unsupported-edge" => valid.Replace("STRAIGHT_JOIN (", "LEFT JOIN (", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var failure = Record.Exception(() => AncestorIndexHydrationJoins.Read(sql, "FixtureNodes"));

        // Assert
        Assert.IsType<FormatException>(failure);
    }

    /// <summary>Builds only the documented fixture grammar, with two stable directed edges in every valid variant.</summary>
    private static string FixtureSql(bool derived, bool swapped)
    {
        var payload = derived
            ? "(SELECT `n`.`Id` FROM `FixtureNodes` AS `n` WHERE (`n`.`Visible`))"
            : "`FixtureNodes`";

        var sourceJoin = swapped ? "STRAIGHT_JOIN" : "INNER JOIN";
        var ownerJoin = swapped ? "INNER JOIN" : "STRAIGHT_JOIN";

        return $"""
                SELECT `source`.`Id` AS `Source`, `owner`.`Id` AS `Owner`, 0 AS `Distance`
                FROM `modes` AS `m`
                STRAIGHT_JOIN `candidates` AS `c` ON 1 = 1
                {sourceJoin} {payload} AS `source` ON 1 = 1
                {ownerJoin} {payload} AS `owner` ON 1 = 1
                """;
    }
}
