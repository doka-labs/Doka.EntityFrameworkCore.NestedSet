namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns isolated ordering tables while reusing the established real database provisioning.</summary>
public sealed class OrderingKeyFixture : IAsyncLifetime
{
    /// <summary>Retains one disposable database for each engine used by this test class.</summary>
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Clears the fixture's key-specific tables and returns a context owned by the caller.</summary>
    /// <param name="engine">The requested relational engine.</param>
    /// <returns>A context connected to the empty key-ordering tables.</returns>
    public async Task<OrderingKeyContext> ResetAsync(
        string engine
    )
    {
        if (!_databases.TryGetValue(engine, out var database))
        {
            database = await TestDatabase.CreateAsync(engine);
            _databases.Add(engine, database);

            await using var setup = await CreateContextAsync(engine);
            await setup
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);
        }

        var context = await CreateContextAsync(engine);
        await context
            .Set<OrderingKeyNode<string, string?>>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (string?)null),
                CancellationToken.None);

        await context
            .Set<OrderingKeyNode<string, string?>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingKeyNode<Guid, Guid?>>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (Guid?)null),
                CancellationToken.None);

        await context
            .Set<OrderingKeyNode<Guid, Guid?>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingKeyNode<byte[], byte[]?>>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (byte[]?)null),
                CancellationToken.None);

        await context
            .Set<OrderingKeyNode<byte[], byte[]?>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingKeyNode<int, int?>>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (int?)null),
                CancellationToken.None);
        await context
            .Set<OrderingKeyNode<int, int?>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await NestedSetTestInfrastructure.ClearRegistriesAsync(context, CancellationToken.None);

        return context;
    }

    /// <summary>Uses the provisioned connection with this fixture's independent ordered EF model.</summary>
    private async Task<OrderingKeyContext> CreateContextAsync(
        string engine
    )
    {
        await using var source = _databases[engine]
            .CreateContext();

        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<OrderingKeyContext>()
            .ConfigureTestWarnings()
            .UseNestedSets();

        switch (engine)
        {
            case "Sqlite":
                options.UseSqlite(connection);
                break;
            case "PostgreSql":
                options.UseNpgsql(connection);
                break;
            case "SqlServer":
                options.UseSqlServer(connection);
                break;
            case "MySql":
                options.UseMySql(connection, DatabaseTestTargets.MySql);
                break;
            case "MariaDb":
                options.UseMySql(connection, DatabaseTestTargets.MariaDb);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        return new OrderingKeyContext(options.Options);
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

/// <summary>Maps native and converted ordered identities through ordinary asynchronous save integration.</summary>
public sealed class OrderingKeyContext : NestedSetDbContext
{
    /// <summary>Names the fixture-owned PostgreSQL collation used for case-insensitive key aliases.</summary>
    private const string PostgreSqlKeyCollation = "ordering_key_case_insensitive";

    /// <summary>Creates a context using its fixture's selected relational provider.</summary>
    /// <param name="options">The provider and connection options.</param>
    public OrderingKeyContext(
        DbContextOptions<OrderingKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var text = ConfigureNode<string, string?>(modelBuilder, "OrderingKeyTextNodes");
        ConfigureNode<Guid, Guid?>(modelBuilder, "OrderingKeyGuidNodes");
        ConfigureNode<byte[], byte[]?>(modelBuilder, "OrderingKeyBinaryNodes");
        var converted = ConfigureNode<int, int?>(modelBuilder, "OrderingKeyConvertedNodes");
        converted
            .Property(node => node.Id)
            .HasConversion<string>()
            .HasMaxLength(64);
        converted
            .Property(node => node.ParentId)
            .HasConversion<string>()
            .HasMaxLength(64);

        if (Database.IsNpgsql())
        {
            // WHY: PostgreSQL requires a nondeterministic ICU collation for case-insensitive identity equality.
            modelBuilder.HasCollation(
                PostgreSqlKeyCollation,
                locale: "und-u-ks-level2",
                provider: "icu",
                deterministic: false);
        }

        var keyCollation = Database.IsSqlite()
            ? "NOCASE"
            : Database.IsNpgsql()
                ? PostgreSqlKeyCollation
                : Database.IsSqlServer()
                    ? "Latin1_General_100_CI_AS"
                    : "utf8mb4_general_ci";

        var parentCollation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsNpgsql()
                ? "C"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_BIN2"
                    : "utf8mb4_bin";

        text
            .Property(node => node.Id)
            .UseCollation(keyCollation);

        // WHY: A different parent collation exposes lookups that incorrectly compare aliases using the FK column.
        text
            .Property(node => node.ParentId)
            .UseCollation(parentCollation);
    }

    /// <summary>Maps one standalone hierarchy before applying any key-specific storage facets.</summary>
    private static EntityTypeBuilder<OrderingKeyNode<TKey, TParent>> ConfigureNode<TKey, TParent>(
        ModelBuilder modelBuilder,
        string table
    )
        where TKey : notnull
    {
        var node = modelBuilder.Entity<OrderingKeyNode<TKey, TParent>>();
        node.ToTable(table);
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();
        node
            .Property(value => value.ParentId)
            .IsRequired(false);
        node
            .Property(value => value.Name)
            .HasMaxLength(128);

        if (typeof(TKey) == typeof(string)
            || typeof(TKey) == typeof(byte[]))
        {
            node
                .Property(value => value.Id)
                .HasMaxLength(64);
            node
                .Property(value => value.ParentId)
                .HasMaxLength(64);
        }

        node.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId)
            .OrderBy(value => value.Name));

        return node;
    }
}

/// <summary>Stores one ordered hierarchy with a mapped identity and matching nullable parent representation.</summary>
/// <typeparam name="TKey">The model identity type, including explicitly converted keys.</typeparam>
/// <typeparam name="TParent">The identity's nullable parent type.</typeparam>
public sealed class OrderingKeyNode<TKey, TParent> : IScopedNestedSetNode<TKey, Guid, int>
    where TKey : notnull
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Creates a node whose coordinates and scope will be assigned by the hierarchy service.</summary>
    /// <param name="id">The application-assigned identity.</param>
    /// <param name="name">The domain value used for sibling ordering.</param>
    public OrderingKeyNode(
        TKey id,
        string name
    )
    {
        Id = id;
        Name = name;
    }

    /// <inheritdoc />
    public TKey Id { get; set; }

    /// <summary>Gets or sets the independent hierarchy scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the nullable direct-parent identity.</summary>
    public TParent? ParentId { get; set; }

    /// <summary>Gets or sets the ascending domain criterion.</summary>
    public string Name { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
