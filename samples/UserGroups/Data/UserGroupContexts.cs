namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Creates short-lived application contexts for the selected provider.</summary>
internal static class UserGroupContexts
{
    /// <summary>Creates the concrete context that owns the selected provider's migration chain.</summary>
    /// <param name="configuration">The sample-owned database configuration.</param>
    /// <param name="readOnly">Whether SQLite opens an existing result without write or create access.</param>
    /// <returns>A context the caller must dispose.</returns>
    public static UserGroupContext Create(
        SampleDatabaseConfiguration configuration,
        bool readOnly = false
    )
    {
        if (configuration.Provider != SampleProvider.Sqlite)
        {
            return new UserGroupContext(configuration.CreateOptions<UserGroupContext>(readOnly));
        }

        var context = new UserGroupSqliteContext(configuration.CreateOptions<UserGroupSqliteContext>(readOnly));
        var connection = new SqliteConnectionStringBuilder(context.Database.GetDbConnection().ConnectionString)
        {
            Pooling = false,
        };

        // WHY: Each invocation owns its SQLite file. Context disposal must release its handles before caller
        // cleanup; a process-wide pool clear would also disturb unrelated concurrent sample invocations.
        context.Database.SetConnectionString(connection.ConnectionString);

        return context;
    }
}
