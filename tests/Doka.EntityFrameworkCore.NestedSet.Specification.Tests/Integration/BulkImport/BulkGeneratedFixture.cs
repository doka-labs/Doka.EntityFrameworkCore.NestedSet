namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns generated-key, generated-sort and immediate-FK import tables on each real engine.</summary>
public sealed class BulkGeneratedFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Clears the import mapping and returns an independent context with optional command observers.</summary>
    public async Task<BulkGeneratedContext> ResetAsync(
        string engine,
        params IInterceptor[] interceptors
    )
    {
        var create = !_databases.TryGetValue(engine, out var database);

        if (create)
        {
            database = await TestDatabase.CreateAsync(engine);
            _databases.Add(engine, database);
        }

        await using var source = database!.CreateContext();
        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<BulkGeneratedContext>()
            .ConfigureTestWarnings()
            .UseNestedSets();

        switch (engine)
        {
            case "Sqlite":
                options.UseSqlite(connection);
                break;
            case "MySql":
                options.UseMySql(connection, DatabaseTestTargets.MySql);
                break;
            case "MariaDb":
                options.UseMySql(connection, DatabaseTestTargets.MariaDb);
                break;
            case "PostgreSql":
                options.UseNpgsql(connection);
                break;
            case "SqlServer":
                options.UseSqlServer(connection);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        await using (var setup = new BulkGeneratedContext(options.Options))
        {
            if (create)
            {
                await setup
                    .GetService<IRelationalDatabaseCreator>()
                    .CreateTablesAsync(CancellationToken.None);

                if (engine == "Sqlite")
                {
                    // WHY: The trigger changes a generated token after each direct parent or coordinate update.
                    await setup.Database.ExecuteSqlRawAsync(
                        "CREATE TRIGGER BulkVersion AFTER UPDATE ON BulkGeneratedNodes "
                        + "WHEN NEW.Version = OLD.Version BEGIN UPDATE BulkGeneratedNodes "
                        + "SET Version = OLD.Version + 1, Details_Revision = OLD.Details_Revision + 1 "
                        + "WHERE Id = NEW.Id; END",
                        CancellationToken.None);
                }
            }

            await setup
                .Set<BulkGeneratedNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    CancellationToken.None);
            await setup
                .Set<BulkGeneratedNode>()
                .ExecuteDeleteAsync(CancellationToken.None);
            await setup
                .Set<BulkManualNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    CancellationToken.None);
            await setup
                .Set<BulkManualNode>()
                .ExecuteDeleteAsync(CancellationToken.None);
            await setup
                .Set<BulkNavigationNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    CancellationToken.None);
            await setup
                .Set<BulkNavigationNode>()
                .ExecuteDeleteAsync(CancellationToken.None);
            // WHY: Fresh import cases must not inherit active identities or tombstones from a previous case.
            await NestedSetTestInfrastructure.ClearRegistriesAsync(setup, CancellationToken.None);
        }

        return new BulkGeneratedContext(
            options.AddInterceptors(interceptors)
                .Options);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }
    }
}

/// <summary>Maps immediate nullable parent FKs without navigations and generated keys and sort defaults.</summary>
public sealed class BulkGeneratedContext : NestedSetDbContext
{
    /// <summary>Creates a context using fixture-owned database options.</summary>
    public BulkGeneratedContext(
        DbContextOptions<BulkGeneratedContext> options
    ) : base(options) { }

    /// <summary>Gets or sets a test callback that inspects the tracker after the base save returns.</summary>
    public Action<DbContext>? AfterBaseSave { get; set; }

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        AfterBaseSave?.Invoke(this);

        return saved;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<BulkGeneratedNode>();
        node.ToTable("BulkGeneratedNodes");
        node
            .Property(value => value.Name)
            .HasMaxLength(80)
            .HasDefaultValue("Generated");
        var details = node.ComplexProperty(value => value.Details);
        details
            .Property(value => value.Label)
            .HasMaxLength(80)
            .HasDefaultValue("Generated detail");

        if (Database.IsSqlite())
        {
            // WHY: SQLite RETURNING precedes AFTER triggers; EF must select the final generated token after the update.
            node.ToTable(table => table.UseSqlReturningClause(false));
            node
                .Property(value => value.Version)
                .HasDefaultValue(0)
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            details
                .Property(value => value.Revision)
                .HasDefaultValue(0)
                .ValueGeneratedOnAddOrUpdate();
            node
                .ComplexCollection(value => value.History)
                .ToJson();
        }
        else
        {
            node.Ignore(value => value.Version);
            node.Ignore(value => value.History);
        }

        node.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId)
            .OrderBy(value => value.Name, NullSortOrder.Last));

        var manual = modelBuilder.Entity<BulkManualNode>();
        manual.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));

        var navigation = modelBuilder.Entity<BulkNavigationNode>();
        navigation
            .Property(value => value.Id)
            .ValueGeneratedNever();
        navigation
            .HasOne(value => value.Parent)
            .WithMany(value => value.Children)
            .HasForeignKey(value => new
            {
                value.Scope,
                value.ParentId
            })
            .HasPrincipalKey(value => new
            {
                value.Scope,
                value.Id
            })
            .OnDelete(DeleteBehavior.Restrict);
        navigation.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));
    }
}

/// <summary>Receives its key and optional sort value from the database during the shared insertion save.</summary>
public sealed class BulkGeneratedNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the nullable immediate foreign key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the provider-generated default or caller-assigned sort value.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the SQLite trigger-generated optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets inline complex payload whose generated leaves follow the mapped value contract.</summary>
    public BulkGeneratedDetails Details { get; set; } = new();

    /// <summary>Gets or sets SQLite JSON complex elements used to verify complete mapped-value rollback.</summary>
    public List<BulkGeneratedDetails> History { get; set; } = [];

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}

/// <summary>Stores complex scalar defaults and trigger values in the hierarchy table.</summary>
public sealed class BulkGeneratedDetails
{
    /// <summary>Gets or sets the database-generated default inside the complex payload.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets the SQLite trigger-generated value changed by structural bulk updates.</summary>
    public int Revision { get; set; }
}

/// <summary>Receives database identities while retaining explicit input sibling order.</summary>
public sealed class BulkManualNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the immediate nullable parent foreign key.</summary>
    public int? ParentId { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}

/// <summary>Maps a full CLR self-navigation graph to verify that failed imports leave input graphs reusable.</summary>
public sealed class BulkNavigationNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the nullable parent foreign key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the optional mapped parent navigation.</summary>
    public BulkNavigationNode? Parent { get; set; }

    /// <summary>Gets the initially empty mapped child navigation.</summary>
    public List<BulkNavigationNode> Children { get; } = [];

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
