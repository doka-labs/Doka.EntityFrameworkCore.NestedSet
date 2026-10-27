namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Keeps the optional SQLite migration history separate from the Doka MySQL/MariaDB model.</summary>
public sealed class SignalLabSqliteContext : SignalLabContext
{
    /// <summary>Creates the SQLite context without opening or changing its database.</summary>
    /// <param name="options">The configured SQLite and NestedSet options.</param>
    public SignalLabSqliteContext(
        DbContextOptions<SignalLabSqliteContext> options
    ) : base(options) { }
}
