namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects the real chain transaction contract from producing an empty, skipped SQLite theory.</summary>
public sealed class GroupSupervisorDiscoveryTests
{
    /// <summary>
    /// Unsupported SQLite cases disappear through fact selection while every server retains the test.
    /// </summary>
    /// <param name="engine">The engine whose eligibility is being observed.</param>
    /// <param name="expected">Whether the actual shared transaction contract applies to this engine.</param>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    public void InadequateChainTransactionUsesFactSelectionWithoutEmptyData(
        string engine,
        bool expected
    )
    {
        // Arrange
        var method = typeof(GroupSupervisorTests)
            .GetMethod(nameof(GroupSupervisorTests.ReadUncommittedChainTransactionIsRejectedBeforeFirstReader))!;

        var attribute = Assert.Single(method.GetCustomAttributes<FactAttribute>());

        // Act
        var applies = attribute is EngineFactAttribute fact
            && EngineTestSelection.AppliesToEngine(engine, fact.ExcludedEngines, fact.Reason);

        // Assert
        Assert.IsType<EngineFactAttribute>(attribute);
        Assert.Equal(expected, applies);
        Assert.Null(attribute.Skip);
        Assert.Empty(method.GetParameters());
        Assert.Empty(method.GetCustomAttributes<DataAttribute>());
    }
}
