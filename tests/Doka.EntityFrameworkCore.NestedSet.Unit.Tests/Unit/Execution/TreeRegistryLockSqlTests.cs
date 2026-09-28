namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies provider-specific typed tree-registry lock SQL fragments.</summary>
public sealed class TreeRegistryLockSqlTests
{
    /// <summary>Builds PostgreSQL and SQLite rowsets without SQL Server's JSON transport.</summary>
    [Fact]
    public void RelationalValuesSourceUsesDirectCommonTableExpression()
    {
        // Arrange
        const string rows = "(@scope0, @tree0, 0, 1), (@scope1, @tree1, 1, 0)";

        // Act
        var sql = Execution.NestedSetTreeLocks.BuildSourceCte(
            Providers.NestedSetProviderKind.PostgreSql,
            "\"RequestedTrees\"",
            "[Scope], [TreeId], [Ordinal], [Create]",
            rows);

        // Assert
        Assert.Equal(
            "WITH \"RequestedTrees\" ([Scope], [TreeId], [Ordinal], [Create]) AS (VALUES "
            + rows
            + ") ",
            sql);
    }
}
