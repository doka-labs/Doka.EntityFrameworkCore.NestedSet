namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns real provider databases while runtime contexts consume generated models exclusively.</summary>
public sealed class CompiledModelFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _initialized = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Builds schema through ordinary metadata, then returns a context that forbids rebuilding.</summary>
    /// <param name="engine">The real relational engine represented by the generated model.</param>
    /// <param name="compiledModel">The checked-in output of that provider's actual EF model generator.</param>
    public async Task<CompiledTreeContext> ResetAsync(
        string engine,
        IModel compiledModel
    )
    {
        await using var setup = await CreateContextAsync(engine, null);

        if (!_initialized.Contains(engine))
        {
            // WHY: Schema creation needs design metadata; runtime operations below must use only generated metadata.
            await setup
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);

            _initialized.Add(engine);
        }

        // WHY: Both hierarchy tables have restrictive self foreign keys. A previous case can leave a child row,
        // so detach parent references before deleting either table during fixture reuse.
        await setup
            .Set<CompiledFolder>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ParentId, (string?)null),
                CancellationToken.None);

        await setup
            .Set<CompiledNumber>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ParentId, (int?)null),
                CancellationToken.None);

        await setup
            .Set<CompiledFolder>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await setup
            .Set<CompiledNumber>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await setup
            .Set<CompiledScope>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await NestedSetTestInfrastructure.ClearRegistriesAsync(setup, CancellationToken.None);
        await setup.AddAsync(new CompiledScope { Id = "scope" }, CancellationToken.None);
        await setup.SaveChangesAsync(CancellationToken.None);

        return await CreateContextAsync(engine, compiledModel);
    }

    /// <summary>Retains the fixture's exact provider options and replaces only its explicit model selection.</summary>
    /// <typeparam name="TContext">The context that consumes the cloned provider configuration.</typeparam>
    /// <param name="engine">The provider whose configured extensions are cloned.</param>
    /// <param name="model">The optional generated runtime model.</param>
    /// <param name="enableNestedSets">Whether the primary nested-set options bootstrap is installed.</param>
    /// <returns>The complete options for the requested context.</returns>
    public async Task<DbContextOptions<TContext>> CreateOptionsAsync<TContext>(
        string engine,
        IModel? model = null,
        bool enableNestedSets = true
    )
        where TContext : DbContext
    {
        if (!_databases.TryGetValue(engine, out var database))
        {
            database = await TestDatabase.CreateAsync(engine);
            _databases.Add(engine, database);
        }

        await using var source = database.CreateContext();
        var extensions = source
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        if (!enableNestedSets)
        {
            // WHY: The shared provider context enables the extension. A negative compiled-model case must remove
            // that exact cloned extension before constructing the context whose finalization hook is intentionally
            // absent.
            extensions.Remove(typeof(NestedSetOptionsExtension));
        }

        var options = new DbContextOptionsBuilder<TContext>(new DbContextOptions<TContext>(extensions))
            .ConfigureTestWarnings();

        if (enableNestedSets)
        {
            options.UseNestedSets();
        }

        return model is null
            ? options.Options
            : options.UseModel(model).Options;
    }

    /// <summary>Creates the context type whose provider generated the selected runtime model.</summary>
    private async Task<CompiledTreeContext> CreateContextAsync(
        string engine,
        IModel? model
    ) => engine switch
    {
        "Sqlite" =>
            new CompiledTreeContext(await CreateOptionsAsync<CompiledTreeContext>(engine, model), model is not null),
        "MySql" => new CompiledMySqlContext(
            await CreateOptionsAsync<CompiledMySqlContext>(engine, model),
            model is not null),
        "MariaDb" => new CompiledMariaDbContext(
            await CreateOptionsAsync<CompiledMariaDbContext>(engine, model),
            model is not null),
        "PostgreSql" => new CompiledPostgreSqlContext(
            await CreateOptionsAsync<CompiledPostgreSqlContext>(engine, model),
            model is not null),
        "SqlServer" => new CompiledSqlServerContext(
            await CreateOptionsAsync<CompiledSqlServerContext>(engine, model),
            model is not null),
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }
    }
}
