namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class MigrationDatabase
{
    /// <summary>Creates an independent context using the real provider and optional integration.</summary>
    /// <param name="stage">The historical model version.</param>
    /// <param name="migrationsAssembly">The generated assembly, when migrations already exist.</param>
    /// <returns>A context owned by the caller.</returns>
    public MigrationContext CreateContext(
        MigrationStage stage = MigrationStage.Current,
        Assembly? migrationsAssembly = null
    )
    {
        var options = new DbContextOptionsBuilder<MigrationContext>();
        migrationsAssembly ??= typeof(MigrationContext).Assembly;

        if (Engine == "Sqlite")
        {
            options.UseSqlite(
                _connection,
                x => x
                    .CommandTimeout(0)
                    .UseNestedSets()
                    .MigrationsAssembly(migrationsAssembly));
        }
        else if (Engine == "PostgreSql")
        {
            options.UseNpgsql(
                _connection,
                x => x
                    .CommandTimeout(0)
                    .UseNestedSets()
                    .MigrationsAssembly(migrationsAssembly));
        }
        else if (Engine == "SqlServer")
        {
            options.UseSqlServer(
                _connection,
                x => x
                    .CommandTimeout(0)
                    .UseNestedSets()
                    .MigrationsAssembly(migrationsAssembly));
        }
        else
        {
            var version = Engine == "MariaDb"
                ? DatabaseTestTargets.MariaDb
                : DatabaseTestTargets.MySql;

            options.UseMySql(
                _connection,
                version,
                x => x
                    .UseNestedSets()
                    .MigrationsAssembly(migrationsAssembly));
        }

        // WHY: Every compiled test assembly and schema is unique; retaining their service providers globally
        // would accumulate test-only models across otherwise independent cases.
        options.EnableServiceProviderCaching(false);
        options.ReplaceService<IModelCacheKeyFactory, MigrationModelCacheKeyFactory>();
        _configureOptions?.Invoke(options);

        var context = new MigrationContext(options.Options, stage, Schema);
        FunctionalTestContextPolicy.Configure(context);

        return context;
    }

    /// <summary>Runs EF's scaffolder and compiles the complete generated migration, metadata and snapshot.</summary>
    /// <param name="stage">The model to compare against the preceding snapshot.</param>
    /// <param name="name">The new migration name.</param>
    /// <param name="priorChain">The compiled preceding migrations and snapshot, if any.</param>
    /// <returns>A complete compiled migration chain.</returns>
    public MigrationChain Scaffold(
        MigrationStage stage,
        string name,
        MigrationChain? priorChain = null
    )
    {
        using var context = CreateContext(stage, priorChain?.Assembly);
        var services = new ServiceCollection();
        services.AddDbContextDesignTimeServices(context);

        var provider = Assembly.Load(context.Database.ProviderName!);
        var attribute = provider.GetCustomAttribute<DesignTimeProviderServicesAttribute>()
            ?? throw new InvalidOperationException("The provider does not declare its design-time services.");

        var serviceType = provider.GetType(attribute.TypeName, throwOnError: true)!;
        var providerServices = (IDesignTimeServices)Activator.CreateInstance(serviceType)!;
        providerServices.ConfigureDesignTimeServices(services);
        services.AddEntityFrameworkDesignTimeServices();
        _configureDesignServices?.Invoke(services, context);

        using var serviceProvider = services.BuildServiceProvider();
        var scaffolder = serviceProvider.GetRequiredService<IMigrationsScaffolder>();
        var migration = scaffolder.ScaffoldMigration(name, "Doka.Generated", "Migrations", "C#");
        var sources = priorChain?.Sources.ToList() ?? [];
        sources.Add(migration.MigrationCode);
        sources.Add(migration.MetadataCode);

        var migrationIds = priorChain?.MigrationIds.ToList() ?? [];
        migrationIds.Add(migration.MigrationId);

        return new MigrationChain(sources, migration.SnapshotCode, migrationIds, migration.MigrationCode, stage);
    }
}
