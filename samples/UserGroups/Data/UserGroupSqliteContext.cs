namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Uses the same application model with a distinct SQLite migration history.</summary>
public sealed class UserGroupSqliteContext : UserGroupContext
{
    /// <summary>Creates the optional SQLite sample context.</summary>
    /// <param name="options">SQLite options including the nested-set registration.</param>
    public UserGroupSqliteContext(
        DbContextOptions<UserGroupSqliteContext> options
    ) : base(options) { }
}
