namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Provides a context-scoped migration model for testing sample lifecycle behavior without a server.</summary>
internal sealed class SampleLifecycleContext : DbContext
{
    /// <summary>Creates a context from the shared sample configuration.</summary>
    /// <param name="options">The provider and owned connection selected by the sample boundary.</param>
    public SampleLifecycleContext(
        DbContextOptions<SampleLifecycleContext> options
    ) : base(options) { }

    /// <summary>Gets rows whose preservation exposes unintended reset or inspection writes.</summary>
    public DbSet<SampleLifecycleSentinel> Sentinels => Set<SampleLifecycleSentinel>();

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => ConfigureModel(modelBuilder);

    /// <summary>Keeps the tiny fixture's runtime, migration target and snapshot structurally identical.</summary>
    /// <param name="modelBuilder">The model being constructed through EF's public metadata API.</param>
    internal static void ConfigureModel(
        ModelBuilder modelBuilder
    )
    {
        // WHY: This fixture exercises lifecycle operations, so its single frozen table needs no hierarchy machinery.
        modelBuilder.Entity<SampleLifecycleSentinel>(entity =>
        {
            entity.ToTable("SampleSentinels");
            entity.HasKey(sentinel => sentinel.Id);
            entity
                .Property(sentinel => sentinel.Id)
                .ValueGeneratedNever()
                .HasColumnType("INTEGER");

            entity
                .Property(sentinel => sentinel.Value)
                .IsRequired()
                .HasColumnType("TEXT");
        });
    }
}
