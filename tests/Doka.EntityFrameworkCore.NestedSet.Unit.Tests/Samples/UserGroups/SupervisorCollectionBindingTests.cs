namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.UserGroups;

/// <summary>Separates catalog array membership from server-side membership subqueries.</summary>
public sealed class SupervisorCollectionBindingTests
{
    /// <summary>Catalogs bind explicitly while origin membership retains its queryable semijoin.</summary>
    /// <param name="engine">The configured provider whose original expression graph is inspected.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public async Task CatalogCollectionsUseEnumerableAndOriginsRemainQueryable(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<UserGroupContext>(
                ModelCompatibilityDatabase.Options<UserGroupContext>(engine))
            .AddInterceptors(boundary, boundary.Commands);

        await using var context = new UserGroupContext(options.Options);
        var request = new SupervisorRequest
        {
            SourceUserId = 1,
            SearchStart = SearchStart.ParentsOnly,
            Roles = new NoRoleFilter(),
            Groups = new AllEligibleGroups([1, 2]),
            Matches =
            [
                new(MetadataOwner.SourceUser, "Route", MetadataOwner.CandidateMembership, "Route"),
                new(MetadataOwner.SourceGroup, "ChannelClass", MetadataOwner.CandidateGroup, "ChannelClass"),
            ],
        };

        var probe = new CollectionMembershipProbe();

        // Act
        var query = SupervisorQueries.ResolutionRows(context, Guid.NewGuid(), request, SupervisorSelection.All);
        probe.Visit(query.Expression);

        // Assert
        Assert.NotEmpty(probe.Memberships);
        Assert.All(probe.Memberships, call => Assert.Equal(typeof(Enumerable), call.Method.DeclaringType));
        Assert.True(probe.ParameterMarkers > 0);
        Assert.True(probe.QueryableMemberships > 0);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }
}
