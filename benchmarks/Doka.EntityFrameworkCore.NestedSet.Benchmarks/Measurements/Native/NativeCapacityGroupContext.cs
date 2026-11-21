using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Runs the actual supervisor builders over the same persisted capacity hierarchy.</summary>
internal sealed class NativeCapacityGroupContext
    : GroupApp.UserGroupContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public NativeCapacityGroupContext(
        DbContextOptions<NativeCapacityGroupContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder
    )
    {
        base.ConfigureConventions(configurationBuilder);

        // WHY: The real status UNION's unbound StartTreeId literals must retain the same reversible tree
        // mapping as its entity-property branches. Tenant/role properties retain their explicit integer codecs.
        configurationBuilder
            .DefaultTypeMapping<Guid>()
            .HasConversion<NativeCapacityTreeConverter>();
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
        NativeCapacityConsumerModel.Configure(modelBuilder, false);
    }
}
