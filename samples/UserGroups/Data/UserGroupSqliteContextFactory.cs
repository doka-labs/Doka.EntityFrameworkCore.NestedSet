namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Provides database-free EF design-time configuration for the optional SQLite migration chain.</summary>
public sealed class UserGroupSqliteContextFactory : IDesignTimeDbContextFactory<UserGroupSqliteContext>
{
    /// <inheritdoc />
    public UserGroupSqliteContext CreateDbContext(
        string[] args
    )
    {
        var configuration = SampleDatabaseConfiguration.Create("usergroups", SampleProvider.Sqlite);

        return new UserGroupSqliteContext(configuration.CreateOptions<UserGroupSqliteContext>());
    }
}
