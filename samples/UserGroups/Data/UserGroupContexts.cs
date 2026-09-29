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
    ) => configuration.Provider == SampleProvider.Sqlite
        ? new UserGroupSqliteContext(configuration.CreateOptions<UserGroupSqliteContext>(readOnly))
        : new UserGroupContext(configuration.CreateOptions<UserGroupContext>(readOnly));
}
