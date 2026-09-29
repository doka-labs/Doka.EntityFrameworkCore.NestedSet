namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Provides database-free EF design-time configuration for the Doka migration chain.</summary>
public sealed class UserGroupContextFactory : IDesignTimeDbContextFactory<UserGroupContext>
{
    /// <inheritdoc />
    public UserGroupContext CreateDbContext(
        string[] args
    )
    {
        var command = SampleCommand.Parse(args, Program.Scenarios);

        if (command.Provider == SampleProvider.Sqlite)
        {
            throw new ArgumentException("Use UserGroupSqliteContext for SQLite migrations.", nameof(args));
        }

        var configuration = SampleDatabaseConfiguration.Create("usergroups", command.Provider);

        return new UserGroupContext(configuration.CreateOptions<UserGroupContext>());
    }
}
