namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Exposes ordinary metric tracking and the optional NestedSet SaveChanges integration.</summary>
public class KpiContext : NestedSetDbContext
{
    /// <summary>Creates a context from caller-owned provider options without opening the database.</summary>
    /// <param name="options">The configured EF provider and NestedSet services.</param>
    public KpiContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the ordinary EF set used for payload reads and updates.</summary>
    public DbSet<Kpi> Kpis => Set<Kpi>();

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        // WHY: Persist the sample's comparison policy in migrations so database recreation keeps it after --reset.
        if (Database.ProviderName == "Doka.EntityFrameworkCore.MySql")
        {
            modelBuilder.UseCollation("utf8mb4_bin");
        }

        modelBuilder.ApplyConfiguration(new KpiConfiguration());
    }
}
