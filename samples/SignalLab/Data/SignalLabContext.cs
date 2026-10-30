namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Exposes independent synthetic instrument and acquisition sets with optional SaveChanges integration.</summary>
public class SignalLabContext : NestedSetDbContext
{
    /// <summary>Creates a context from caller-owned provider options without opening the database.</summary>
    /// <param name="options">The configured EF provider and NestedSet services.</param>
    public SignalLabContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the ordinary EF set used for payload reads and updates.</summary>
    public DbSet<Instrument> Instruments => Set<Instrument>();

    /// <summary>Gets reporting nodes with shadow structural properties.</summary>
    public DbSet<SignalNode> Nodes => Set<SignalNode>();

    /// <summary>Gets independently defined instrument classifications.</summary>
    public DbSet<NodeKind> NodeKinds => Set<NodeKind>();

    /// <summary>Gets scalar field definitions for each classification.</summary>
    public DbSet<TagDefinition> TagDefinitions => Set<TagDefinition>();

    /// <summary>Gets fixed, case-sensitive field choices.</summary>
    public DbSet<TagChoice> TagChoices => Set<TagChoice>();

    /// <summary>Gets the current scalar metadata owned by individual nodes.</summary>
    public DbSet<TagValue> TagValues => Set<TagValue>();

    /// <summary>Gets inclusive validity periods for exact direct edges.</summary>
    public DbSet<LinkWindow> LinkAvailability => Set<LinkWindow>();

    /// <summary>Gets measurement classifications and additive units.</summary>
    public DbSet<SignalChannel> SignalChannels => Set<SignalChannel>();

    /// <summary>Gets native pulse-counter events.</summary>
    public DbSet<PulseRecord> PulseRecords => Set<PulseRecord>();

    /// <summary>Gets receiver integration-window events.</summary>
    public DbSet<SweepRecord> SweepRecords => Set<SweepRecord>();

    /// <summary>Gets device burst events.</summary>
    public DbSet<BurstRecord> BurstRecords => Set<BurstRecord>();

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

        modelBuilder.ApplyConfiguration(new InstrumentConfiguration());
        ReportingConfiguration.Configure(modelBuilder, this);
    }
}
