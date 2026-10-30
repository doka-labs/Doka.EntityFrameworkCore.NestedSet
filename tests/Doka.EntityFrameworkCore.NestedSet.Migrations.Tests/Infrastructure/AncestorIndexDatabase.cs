using Microsoft.EntityFrameworkCore.Metadata;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Scaffolds and executes the opt-in schema through real provider design-time services.</summary>
public sealed class AncestorIndexDatabase : IAsyncDisposable
{
    private readonly MigrationDatabase _database;
    private readonly string _connection;
    private readonly Action<DbContextOptionsBuilder>? _configureOptions;
    private readonly Action<IServiceCollection, DbContext>? _configureDesignServices;

    private AncestorIndexDatabase(
        MigrationDatabase database,
        string connection,
        Action<DbContextOptionsBuilder>? configureOptions,
        Action<IServiceCollection, DbContext>? configureDesignServices
    )
    {
        _database = database;
        _connection = connection;
        _configureOptions = configureOptions;
        _configureDesignServices = configureDesignServices;
    }

    /// <summary>Gets the existing catalog and direct-insertion support for the same physical table.</summary>
    public MigrationDatabase PhysicalDatabase => _database;

    /// <summary>Creates an empty isolated database without creating tables.</summary>
    /// <param name="fixture">The owner of reusable database engines.</param>
    /// <param name="engine">The actual provider engine.</param>
    /// <param name="configureOptions">Optional SafeMigrations runtime registration.</param>
    /// <param name="configureDesignServices">Optional SafeMigrations design-time registration.</param>
    /// <returns>The isolated ancestor-index migration database.</returns>
    public static async Task<AncestorIndexDatabase> CreateAsync(
        MigrationFixture fixture,
        string engine,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        Action<IServiceCollection, DbContext>? configureDesignServices = null
    )
    {
        var database = await fixture.CreateDatabaseAsync(engine);
        await using var context = database.CreateContext();
        var connection = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The migration fixture did not configure a connection string.");

        return new AncestorIndexDatabase(database, connection, configureOptions, configureDesignServices);
    }

    /// <summary>Creates an independent context for one exact model stage and migration chain.</summary>
    /// <param name="enabled">Whether the ancestor access path is configured.</param>
    /// <param name="assembly">The generated migration assembly, if any.</param>
    /// <param name="adoptIndex">Whether the equivalent index belongs to the application.</param>
    /// <param name="model">An actual generated runtime model, or null for ordinary model building.</param>
    /// <param name="enableIntegration">Whether optional runtime adapters are enabled for this context.</param>
    /// <param name="splitStructure">Whether structural properties occupy a secondary split fragment.</param>
    /// <returns>A context owned by the caller.</returns>
    public AncestorIndexContext CreateContext(
        bool enabled = true,
        Assembly? assembly = null,
        bool adoptIndex = false,
        IModel? model = null,
        bool enableIntegration = true,
        bool splitStructure = false
    )
    {
        var options = new DbContextOptionsBuilder<AncestorIndexContext>();
        assembly ??= typeof(AncestorIndexContext).Assembly;

        switch (_database.Engine)
        {
            case "Sqlite":
                options.UseSqlite(
                    _connection,
                    x => x
                        .UseNestedSets()
                        .MigrationsAssembly(assembly));
                break;
            case "PostgreSql":
                options.UseNpgsql(
                    _connection,
                    x => x
                        .UseNestedSets()
                        .MigrationsAssembly(assembly));
                break;
            case "SqlServer":
                options.UseSqlServer(
                    _connection,
                    x => x
                        .UseNestedSets()
                        .MigrationsAssembly(assembly));
                break;
            default:
                var version = _database.Engine == "MariaDb" ? DatabaseTestTargets.MariaDb : DatabaseTestTargets.MySql;
                options.UseMySql(
                    _connection,
                    version,
                    x => x
                        .UseNestedSets()
                        .MigrationsAssembly(assembly));
                break;
        }

        // WHY: Every generated assembly has a separate model; retaining its provider would retain all earlier chains.
        options.EnableServiceProviderCaching(false);
        options.ReplaceService<IModelCacheKeyFactory, AncestorIndexModelCacheKeyFactory>();
        if (enableIntegration)
        {
            _configureOptions?.Invoke(options);
        }

        if (model is not null)
        {
            options.UseModel(model);
        }

        return new AncestorIndexContext(options.Options, enabled, _database.Schema, adoptIndex, splitStructure);
    }

    /// <summary>Generates and compiles an actual migration and its complete snapshot.</summary>
    /// <param name="enabled">Whether the target schema has the ancestor access path.</param>
    /// <param name="name">The migration name.</param>
    /// <param name="prior">The preceding compiled chain.</param>
    /// <param name="adoptIndex">Whether the equivalent index belongs to the application.</param>
    /// <param name="enableIntegration">Whether optional design and runtime adapters participate.</param>
    /// <param name="splitStructure">Whether structural properties occupy a secondary split fragment.</param>
    /// <returns>A chain executable by the provider's ordinary migration assembly service.</returns>
    public MigrationChain Scaffold(
        bool enabled,
        string name,
        MigrationChain? prior = null,
        bool adoptIndex = false,
        bool enableIntegration = true,
        bool splitStructure = false
    )
    {
        using var context = CreateContext(
            enabled,
            prior?.Assembly,
            adoptIndex,
            enableIntegration: enableIntegration,
            splitStructure: splitStructure);

        var services = new ServiceCollection();
        services.AddDbContextDesignTimeServices(context);
        var provider = Assembly.Load(context.Database.ProviderName!);
        var attribute = provider.GetCustomAttribute<DesignTimeProviderServicesAttribute>()
            ?? throw new InvalidOperationException("The provider does not declare design-time services.");

        var type = provider.GetType(attribute.TypeName, throwOnError: true)!;
        ((IDesignTimeServices)Activator.CreateInstance(type)!).ConfigureDesignTimeServices(services);
        services.AddEntityFrameworkDesignTimeServices();
        if (enableIntegration)
        {
            _configureDesignServices?.Invoke(services, context);
        }

        using var serviceProvider = services.BuildServiceProvider();
        var generated = serviceProvider
            .GetRequiredService<IMigrationsScaffolder>()
            .ScaffoldMigration(name, "Doka.Generated.Ancestor", "Migrations", "C#", false);

        var sources = prior?.Sources.ToList() ?? [];
        sources.Add(generated.MigrationCode);
        sources.Add(generated.MetadataCode);
        var ids = prior?.MigrationIds.ToList() ?? [];
        ids.Add(generated.MigrationId);

        return new MigrationChain(
            sources,
            generated.SnapshotCode,
            ids,
            generated.MigrationCode,
            MigrationStage.Current);
    }

    /// <summary>Applies a compiled chain through the real provider migrator.</summary>
    /// <param name="chain">The generated chain.</param>
    /// <param name="enabled">Whether the target context uses the ancestor access path.</param>
    /// <param name="target">The target migration, or null for the latest migration.</param>
    /// <param name="adoptIndex">Whether the equivalent index belongs to the application.</param>
    /// <param name="enableIntegration">Whether optional runtime adapters participate.</param>
    /// <param name="splitStructure">Whether structural properties occupy a secondary split fragment.</param>
    /// <returns>A task that completes when the provider migration finishes.</returns>
    public async Task MigrateAsync(
        MigrationChain chain,
        bool enabled = true,
        string? target = null,
        bool adoptIndex = false,
        bool enableIntegration = true,
        bool splitStructure = false
    )
    {
        await using var context = CreateContext(
            enabled,
            chain.Assembly,
            adoptIndex,
            enableIntegration: enableIntegration,
            splitStructure: splitStructure);

        await context
            .GetService<IMigrator>()
            .MigrateAsync(target, CancellationToken.None);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
