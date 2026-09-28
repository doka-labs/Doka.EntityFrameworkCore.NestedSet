namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Distinguishes written store columns from provider quoting, CASE keywords, and read-only predicates.
/// </summary>
public sealed class SqlAssignmentsTests
{
    /// <summary>The observer recognizes only assignment targets across supported UPDATE dialects.</summary>
    /// <param name="sql">A representative provider-generated update statement.</param>
    /// <param name="column">The mapped column whose write is being measured.</param>
    /// <param name="expected">Whether the column appears as an assignment target.</param>
    [Theory]
    [InlineData("UPDATE \"TreeNode\" SET \"Start\" = 2 WHERE \"Id\" = 1", "Start", true)]
    [InlineData("UPDATE `TreeNode` AS `t` SET `t`.`End` = 4 WHERE `t`.`Id` = 1", "End", true)]
    [InlineData("UPDATE [t] SET [t].[End] = 4 FROM [TreeNode] AS [t]", "End", true)]
    [InlineData("UPDATE \"TreeNode\" SET \"Position\" = 0, \"end\" = 4", "End", true)]
    [InlineData("UPDATE \"TreeNode\" SET \"Position\" = CASE WHEN \"Start\" = 1 THEN 0 ELSE 1 END", "End", false)]
    [InlineData("UPDATE \"TreeNode\" SET \"Position\" = CASE WHEN \"Start\" = 1 THEN 0 ELSE 1 END", "Start", false)]
    [InlineData("UPDATE \"TreeNode\" SET \"Position\" = 0 WHERE \"End\" = 4", "End", false)]
    [InlineData("UPDATE \"TreeNode\" SET \"Position\" = \"Depth\" + 1", "Depth", false)]
    public void OnlyAssignmentTargetsCountAsWrites(
        string sql,
        string column,
        bool expected
    )
    {
        // Arrange
        var commandText = sql;

        // Act
        var assigned = SqlAssignments.Assigns(commandText, column);

        // Assert
        Assert.Equal(expected, assigned);
    }
}
