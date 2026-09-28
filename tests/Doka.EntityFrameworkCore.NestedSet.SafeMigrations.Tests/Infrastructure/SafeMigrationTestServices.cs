namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

/// <summary>Configures optional adapters through the entry points used by applications and EF tooling.</summary>
internal static class SafeMigrationTestServices
{
    /// <summary>Enables the adapter that matches the already configured relational provider.</summary>
    internal static void ConfigureOptions(
        DbContextOptionsBuilder options,
        string engine
    )
    {
        switch (engine)
        {
            case "Sqlite":
                options.UseSqliteSafeMigrations();
                break;
            case "PostgreSql":
                options.UsePostgreSqlSafeMigrations();
                break;
            case "MySql":
            case "MariaDb":
                options.UseMySqlSafeMigrations();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(engine),
                    engine,
                    "No SafeMigrations adapter is configured.");
        }
    }

    /// <summary>Discovers package-generated design registrations for the active provider.</summary>
    internal static void ConfigureDesignServices(
        IServiceCollection services,
        DbContext context
    )
    {
        // WHY: Discovering the buildTransitive attributes proves a package consumer gets the real EF tooling extension.
        var provider = context.Database.ProviderName;
        var registrations = typeof(SafeMigrationTestServices)
            .Assembly
            .GetCustomAttributes<DesignTimeServicesReferenceAttribute>()
            .Where(attribute => attribute.ForProvider == provider)
            .ToArray();

        if (registrations.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one package-generated SafeMigrations design registration for provider '{provider}', "
                + $"but found {registrations.Length}. Check this test project's direct EF Design package reference "
                + "and generated buildTransitive registration attributes.");
        }

        var serviceType = Type.GetType(registrations[0].TypeName, throwOnError: true)!;
        var designServices = (IDesignTimeServices)Activator.CreateInstance(serviceType)!;
        designServices.ConfigureDesignTimeServices(services);
    }

    /// <summary>Executes actual scaffolded operations independently of the migration-history short circuit.</summary>
    internal static async Task ExecuteOperationsAsync(
        DbContext context,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        // WHY: Calling Migrate again would only test history; replay must reach the provider's runtime guards.
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, context.Model);

        var connection = context.GetService<IRelationalConnection>();

        foreach (var command in commands)
        {
            await command.ExecuteNonQueryAsync(connection, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>Replaces a generated index with an incompatible definition to test fail-closed execution.</summary>
    internal static async Task IntroduceIndexDriftAsync(
        DbContext context,
        ExpectedIndexDefinition definition,
        string wrongColumn
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var index = sql.DelimitIdentifier(definition.Name);
        var table = sql.DelimitIdentifier(definition.Table, definition.Schema);
        var drop = context.Database.ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" => "DROP INDEX " + index,
            "Npgsql.EntityFrameworkCore.PostgreSQL" =>
                "DROP INDEX " + sql.DelimitIdentifier(definition.Name, definition.Schema),
            "Doka.EntityFrameworkCore.MySql" => "DROP INDEX " + index + " ON " + table,
            _ => throw new InvalidOperationException("No index drift syntax is configured for this provider."),
        };

        await context.Database.ExecuteSqlRawAsync(drop, CancellationToken.None);
        await CreateConflictingIndexAsync(context, definition, wrongColumn);
    }

    /// <summary>Creates a conflicting index before its expected generated operation has been applied.</summary>
    internal static async Task CreateConflictingIndexAsync(
        DbContext context,
        ExpectedIndexDefinition definition,
        string wrongColumn
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var index = sql.DelimitIdentifier(definition.Name);
        var table = sql.DelimitIdentifier(definition.Table, definition.Schema);
        var column = sql.DelimitIdentifier(wrongColumn);

        // WHY: Identifiers are provider-delimited; they cannot be SQL parameters. Raw DDL deliberately bypasses
        // SafeMigrations safeguards so the test can represent independently introduced schema drift.
        var create = "CREATE INDEX " + index + " ON " + table + " (" + column + ")";
        await context.Database.ExecuteSqlRawAsync(create, CancellationToken.None);
    }
}
