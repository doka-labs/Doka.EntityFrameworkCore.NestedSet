using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Runs the actual signal builders against shared capacity views and bounded payload tables.</summary>
internal sealed class NativeCapacitySignalContext
    : SignalApp.SignalLabContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public NativeCapacitySignalContext(
        DbContextOptions<NativeCapacitySignalContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder
    )
    {
        base.ConfigureConventions(configurationBuilder);

        // WHY: The real catalog UNION also projects unbound Guid.Empty literals. Their default scalar
        // mapping must match the adapted integer property mapping on every provider, including constants.
        configurationBuilder
            .DefaultTypeMapping<Guid>()
            .HasConversion<NativeCapacityIdentityConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        if (Database.IsNpgsql())
        {
            modelBuilder.HasDefaultSchema("hierarchies");
        }

        base.OnModelCreating(modelBuilder);
        NativeCapacityConsumerModel.Configure(modelBuilder, true);
    }
}
