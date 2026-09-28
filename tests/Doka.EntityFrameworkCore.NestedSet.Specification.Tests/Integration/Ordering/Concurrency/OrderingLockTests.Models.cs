namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns isolated scope-anchor test tables on the existing real-engine infrastructure.</summary>
public sealed class OrderingLockFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Returns an empty anchored-ordering context without disturbing another fixture's tables.</summary>
    /// <param name="engine">The real relational engine.</param>
    /// <returns>A context owned by the test.</returns>
    public async Task<OrderingLockContext> ResetAsync(
        string engine
    )
    {
        if (!_databases.ContainsKey(engine))
        {
            _databases.Add(engine, await TestDatabase.CreateAsync(engine));
            await using var setup = await CreateContextAsync(engine);
            await setup
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);
        }

        var context = await CreateContextAsync(engine);
        await UnlinkAsync<int, FirstLockHierarchy>(context);
        await UnlinkAsync<int, SecondLockHierarchy>(context);
        await UnlinkAsync<string, FirstLockHierarchy>(context);
        await UnlinkAsync<byte[], FirstLockHierarchy>(context);
        await context
            .Set<OrderingLockNode<int, FirstLockHierarchy>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingLockNode<int, SecondLockHierarchy>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingLockNode<string, FirstLockHierarchy>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingLockNode<byte[], FirstLockHierarchy>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingLockAnchor<int>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingLockAnchor<string>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingLockAnchor<byte[]>>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await NestedSetTestInfrastructure.ClearRegistriesAsync(context, CancellationToken.None);

        return context;
    }

    /// <summary>Removes restrictive self references before provider-portable fixture cleanup.</summary>
    private static Task<int> UnlinkAsync<TScope, THierarchy>(
        OrderingLockContext context
    )
        where TScope : notnull => context
        .Set<OrderingLockNode<TScope, THierarchy>>()
        .Where(node => node.ParentId != null)
        .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.ParentId, (int?)null), CancellationToken.None);

    /// <summary>Creates another independently tracked context with a test-owned command observer.</summary>
    /// <param name="engine">The fixture's initialized relational engine.</param>
    /// <param name="interceptors">Optional observers attached only to the returned context.</param>
    /// <returns>A context owned by the caller.</returns>
    public async Task<OrderingLockContext> CreateContextAsync(
        string engine,
        params IInterceptor[] interceptors
    )
    {
        await using var source = _databases[engine].CreateContext();
        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<OrderingLockContext>().ConfigureTestWarnings();

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

        options.UseNestedSets();
        options.AddInterceptors(interceptors);

        return new OrderingLockContext(options.Options);
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

/// <summary>Maps multiple ordered hierarchies to shared integer, string, and binary scope anchors.</summary>
public sealed class OrderingLockContext : NestedSetDbContext
{
    /// <summary>Creates the anchored test context with its real provider and optional observers.</summary>
    /// <param name="options">The provider and observer configuration.</param>
    public OrderingLockContext(
        DbContextOptions<OrderingLockContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        ConfigureAnchor<int>(modelBuilder, "OrderingIntegerAnchors");
        ConfigureAnchor<string>(modelBuilder, "OrderingStringAnchors");
        ConfigureAnchor<byte[]>(modelBuilder, "OrderingBinaryAnchors");
        ConfigureNode<int, FirstLockHierarchy>(modelBuilder, "OrderingFirstNodes");
        ConfigureNode<int, SecondLockHierarchy>(modelBuilder, "OrderingSecondNodes");
        ConfigureNode<string, FirstLockHierarchy>(modelBuilder, "OrderingStringNodes");
        ConfigureNode<byte[], FirstLockHierarchy>(modelBuilder, "OrderingBinaryNodes");
    }

    /// <summary>Maps a stable application-owned scope key with matching native storage semantics.</summary>
    private void ConfigureAnchor<TScope>(
        ModelBuilder modelBuilder,
        string table
    )
        where TScope : notnull
    {
        var anchor = modelBuilder.Entity<OrderingLockAnchor<TScope>>();
        anchor.ToTable(table);
        var key = anchor
            .Property(row => row.Id)
            .ValueGeneratedNever();

        ConfigureScope(key);
    }

    /// <summary>Maps one standalone closed CLR type to the anchor shared by its scope type.</summary>
    private void ConfigureNode<TScope, THierarchy>(
        ModelBuilder modelBuilder,
        string table
    )
        where TScope : notnull
    {
        var node = modelBuilder.Entity<OrderingLockNode<TScope, THierarchy>>();
        node.ToTable(table);
        node
            .Property(row => row.Id)
            .ValueGeneratedNever();
        node
            .Property(row => row.Name)
            .HasMaxLength(64);
        ConfigureScope(
            node
                .Property(row => row.Scope)
                .IsRequired());
        node.HasNestedSet(builder => builder
            .HasTreeId(row => row.TreeId)
            .HasScope(row => row.Scope)
            .HasParent(row => row.ParentId)
            .OrderBy(row => row.Name));
    }

    /// <summary>Preserves explicit string equality and identical bounded key types on anchors and nodes.</summary>
    private void ConfigureScope<TScope>(
        PropertyBuilder<TScope> property
    )
        where TScope : notnull
    {
        if (typeof(TScope) == typeof(string)
            || typeof(TScope) == typeof(byte[]))
        {
            property.HasMaxLength(64);
        }

        if (typeof(TScope) == typeof(string))
        {
            // WHY: Aliases must resolve through the same explicit collation on the anchor and hierarchy scope.
            var collation = Database.IsSqlite()
                ? "NOCASE"
                : Database.IsNpgsql()
                    ? "C"
                    : Database.IsSqlServer()
                        ? "Latin1_General_100_CI_AS"
                        : "utf8mb4_unicode_ci";

            property.UseCollation(collation);
        }
    }
}

/// <summary>Marks the first independently mapped hierarchy without introducing EF inheritance.</summary>
internal sealed class FirstLockHierarchy;

/// <summary>Marks the second independently mapped hierarchy sharing integer scope anchors.</summary>
internal sealed class SecondLockHierarchy;

/// <summary>Stores a stable scope identity used by the application-owned lock protocol.</summary>
/// <typeparam name="TScope">The anchor's native scope-key type.</typeparam>
internal sealed class OrderingLockAnchor<TScope>
    where TScope : notnull
{
    /// <summary>Gets or sets the immutable anchor key.</summary>
    public required TScope Id { get; set; }
}

/// <summary>Stores an ordered hierarchy independent of another closed hierarchy type.</summary>
/// <typeparam name="TScope">The scope-key type shared with an application-owned anchor.</typeparam>
/// <typeparam name="THierarchy">The marker separating independent EF entity types.</typeparam>
internal sealed class OrderingLockNode<TScope, THierarchy> : IScopedNestedSetNode<int, Guid, TScope>
    where TScope : notnull
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the persisted scope value, which may be a database-equal alias.</summary>
    public required TScope Scope { get; set; }

    /// <summary>Gets or sets the optional direct parent.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the domain value determining sibling order.</summary>
    public string Name { get; set; } = "";

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
