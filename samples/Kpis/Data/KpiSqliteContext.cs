namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Keeps the optional SQLite migration history separate from the Doka MySQL/MariaDB model.</summary>
public sealed class KpiSqliteContext : KpiContext
{
    /// <summary>Creates the SQLite context without opening or changing its database.</summary>
    /// <param name="options">The configured SQLite and NestedSet options.</param>
    public KpiSqliteContext(
        DbContextOptions<KpiSqliteContext> options
    ) : base(options) { }
}
