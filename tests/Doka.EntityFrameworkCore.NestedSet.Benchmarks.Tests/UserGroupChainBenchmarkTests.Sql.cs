using System.Data;
using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class UserGroupChainBenchmarkTests
{
    /// <summary>Native reference counts remain independent grouped relations before origin correlation.</summary>
    /// <param name="engine">The native MySQL-family SQL translator.</param>
    /// <param name="selection">The person-selection contract whose complete reader is translated.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MariaDb, BenchmarkPersonSelection.RequireUnique)]
    [InlineData(BenchmarkEngine.MariaDb, BenchmarkPersonSelection.HighestBusinessPriority)]
    [InlineData(BenchmarkEngine.MariaDb, BenchmarkPersonSelection.SecondPerson)]
    [InlineData(BenchmarkEngine.MySql, BenchmarkPersonSelection.RequireUnique)]
    [InlineData(BenchmarkEngine.MySql, BenchmarkPersonSelection.HighestBusinessPriority)]
    [InlineData(BenchmarkEngine.MySql, BenchmarkPersonSelection.SecondPerson)]
    public async Task ReferenceRows_NativePersonCount_KeepsDistinctPeopleOutsideCorrelatedDerivedTables(
        BenchmarkEngine engine,
        BenchmarkPersonSelection selection
    )
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        BenchmarkEnvironment.ConfigureProvider(builder, engine, "Server=localhost;Database=unopened;User ID=benchmark");
        await using var context = new UserGroupContext(builder.Options);
        var request = new SupervisorRequest
        {
            SourceUserId = 1,
            StartGroupId = 2,
            SearchStart = SearchStart.ParentsOnly,
            Roles = new MembershipRoleFilter(Guid.NewGuid()),
            Groups = new NthTypeGroupThenValidate(1, 1),
            Matches = [],
        };

        var personSelection = selection switch
        {
            BenchmarkPersonSelection.HighestBusinessPriority => SupervisorSelection.HighestBusinessPriority,
            BenchmarkPersonSelection.SecondPerson => SupervisorSelection.NthPerson(2),
            _ => SupervisorSelection.RequireUnique,
        };

        // Act
        var sql = ReferenceRows(context, Guid.NewGuid(), request, personSelection).ToQueryString();

        // Assert
        Assert.Contains("COUNT(DISTINCT", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Accesses the complete private reference reader without adding a test-only product entrypoint.</summary>
    private static IQueryable ReferenceRows(
        UserGroupContext context,
        Guid tenant,
        SupervisorRequest request,
        SupervisorSelection selection
    ) => Assert.IsAssignableFrom<IQueryable>(typeof(UserGroupChainReference)
        .GetMethod("Rows", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [context, tenant, request, selection]));
}
