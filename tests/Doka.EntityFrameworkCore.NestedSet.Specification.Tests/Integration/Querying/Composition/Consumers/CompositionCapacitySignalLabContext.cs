using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Runs the actual signal builders against shared capacity views and bounded payload tables.</summary>
internal sealed class CompositionCapacitySignalLabContext
    : SignalApp.SignalLabContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public CompositionCapacitySignalLabContext(
        DbContextOptions<CompositionCapacitySignalLabContext> options
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
            .HasConversion<CompositionConsumerIdentityConverter>();
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
        CompositionConsumerModel.Configure(modelBuilder, true);
    }
}
