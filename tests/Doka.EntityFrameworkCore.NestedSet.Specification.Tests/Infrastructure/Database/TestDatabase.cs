namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns an isolated relational test database or temporary SQLite file.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string? _file;
    private readonly DbContextOptions<TreeContext> _options;
    private readonly bool _ownsServerDatabase;

    private TestDatabase(
        DbContextOptions<TreeContext> options,
        string? file = null,
        bool ownsServerDatabase = false
    )
    {
        _options = options;
        _file = file;
        _ownsServerDatabase = ownsServerDatabase;
    }

    /// <summary>Drops the fixture-owned server database or removes its SQLite files.</summary>
    /// <returns>A task that completes after the database resources have been released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_ownsServerDatabase)
        {
            // WHY: The assembly owns the engine, but completed classes must release their schemas and data.
            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync(CancellationToken.None);
        }

        if (_file is not null)
        {
            File.Delete(_file);
            File.Delete(_file + "-wal");
            File.Delete(_file + "-shm");
        }
    }

    /// <summary>Creates an independent context against this database.</summary>
    /// <returns>A context owned by the caller.</returns>
    public TreeContext CreateContext() => new(_options);

    /// <summary>Creates a context whose commands can be observed or fault-injected by a test.</summary>
    /// <param name="interceptor">The command interceptor used by this context only.</param>
    /// <returns>A context owned by the caller.</returns>
    public TreeContext CreateContext(
        DbCommandInterceptor interceptor
    ) => new(
        new DbContextOptionsBuilder<TreeContext>(_options)
            .ConfigureTestWarnings()
            .AddInterceptors(interceptor)
            .Options);

    /// <summary>Creates a context with command and materialization observers owned by one test.</summary>
    /// <param name="interceptors">The observers registered only for the returned context.</param>
    /// <returns>A context owned by the caller.</returns>
    public TreeContext CreateContext(
        params IInterceptor[] interceptors
    )
    {
        var options = new DbContextOptionsBuilder<TreeContext>(_options)
            .ConfigureTestWarnings()
            .AddInterceptors(interceptors);

        var observers = interceptors
            .OfType<EnterpriseProbe>()
            .ToArray();

        if (observers.Length != 0)
        {
            // WHY: Command-only observers must not create an unused singleton materialization service graph.
            options.AddInterceptors(TestMaterializationObserver.Instance);
        }

        var context = new TreeContext(options.Options);

        foreach (var observer in observers)
        {
            TestMaterializationObserver.Instance.Register(context, observer.RecordMaterialization);
        }

        return context;
    }

    /// <summary>Clears test entities and lock rows without restarting the fixture-owned engine.</summary>
    /// <returns>A task that completes when the database is ready for an isolated case.</returns>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();

        // WHY: MySQL and SQLite check restrictive self foreign keys immediately, so every hierarchy is flattened
        // before deletion. This remains valid when xUnit v3 schedules provider theory cases concurrently.
        await context
            .Set<ConstrainedNode>()
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, (int?)null), CancellationToken.None);
        await context
            .Set<TreeNode>()
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.Parent, (int?)null), CancellationToken.None);
        await context
            .Set<GuidNode>()
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, (Guid?)null), CancellationToken.None);
        await context
            .Set<TextNode>()
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, (string?)null), CancellationToken.None);
        await context
            .Set<BinaryNode>()
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, (byte[]?)null), CancellationToken.None);
        await context
            .Set<ConcurrentNode>()
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, (int?)null), CancellationToken.None);
        await context
            .Set<UnscopedQueryNode>()
            .IgnoreQueryFilters()
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, (int?)null), CancellationToken.None);

        await context
            .Set<ConstrainedNode>()
            .ExecuteDeleteAsync(CancellationToken.None);
        await context
            .Set<TreeNode>()
            .ExecuteDeleteAsync(CancellationToken.None);
        await context
            .Set<GuidNode>()
            .ExecuteDeleteAsync(CancellationToken.None);
        await context
            .Set<TextNode>()
            .ExecuteDeleteAsync(CancellationToken.None);
        await context
            .Set<BinaryNode>()
            .ExecuteDeleteAsync(CancellationToken.None);
        await context
            .Set<ConcurrentNode>()
            .ExecuteDeleteAsync(CancellationToken.None);
        await context
            .Set<UnscopedQueryNode>()
            .IgnoreQueryFilters()
            .ExecuteDeleteAsync(CancellationToken.None);
        await context
            .Set<UnrelatedRow>()
            .ExecuteDeleteAsync(CancellationToken.None);
        await context
            .Set<GeneratedAuditRow>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context.ClearNestedSetTreeRegistriesAsync(CancellationToken.None);
    }

    /// <summary>Creates an isolated database on the assembly-owned engine or a SQLite file.</summary>
    /// <param name="engine">Sqlite, MySql, MariaDb, PostgreSql, or SqlServer.</param>
    /// <returns>A database with an independently owned schema or temporary file.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The engine name is unsupported.</exception>
    /// <exception cref="AggregateException">
    /// Initialization and cleanup both failed; both causes are preserved.
    /// </exception>
    public static Task<TestDatabase> CreateAsync(
        string engine
    ) => CreateAsync(engine, null);

    /// <summary>
    /// Owns startup cleanup and permits deterministic initialization failures in fixture lifecycle tests.
    /// </summary>
    internal static async Task<TestDatabase> CreateAsync(
        string engine,
        Func<TreeContext, Task>? initialize
    )
    {
        var options = new DbContextOptionsBuilder<TreeContext>().ConfigureTestWarnings();
        string? file = null;
        TestDatabase? database = null;

        try
        {
            if (engine == "Sqlite")
            {
                file = Path.Combine(Path.GetTempPath(), $"nestedset-{Guid.NewGuid():N}.db");
                options.UseSqlite($"Data Source={file};Pooling=False;Default Timeout=60");
            }
            else if (engine == "PostgreSql")
            {
                var connection = await (await TestDatabaseServers.GetCurrentAsync()).NewConnectionStringAsync(engine);
                options.UseNpgsql(connection);
            }
            else if (engine == "SqlServer")
            {
                var connection = await (await TestDatabaseServers.GetCurrentAsync()).NewConnectionStringAsync(engine);
                options.UseSqlServer(connection);
            }
            else if (engine == "MySql")
            {
                var connection = await (await TestDatabaseServers.GetCurrentAsync()).NewConnectionStringAsync(engine);
                options.UseMySql(connection, DatabaseTestTargets.MySql);
            }
            else if (engine == "MariaDb")
            {
                var connection = await (await TestDatabaseServers.GetCurrentAsync()).NewConnectionStringAsync(engine);
                options.UseMySql(connection, DatabaseTestTargets.MariaDb);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(engine));
            }

            // WHY: Keep the test suite on the documented provider-then-NestedSet registration order so equivalent
            // contexts reuse EF's internal singleton service provider instead of differing only by extension order.
            options.UseNestedSets();

            database = new TestDatabase(options.Options, file, ownsServerDatabase: file is null);
            await using var context = database.CreateContext();
            if (initialize is null)
            {
                await context.Database.EnsureCreatedAsync(CancellationToken.None);
            }
            else
            {
                await initialize(context);
            }

            return database;
        }
        catch (Exception initializationFailure)
        {
            // WHY: Initialization can fail before the caller receives ownership; a failed server start has no database.
            var failed = database ?? new TestDatabase(options.Options, file);

            try
            {
                await failed.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Database fixture initialization and resource cleanup both failed.",
                    initializationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }
}

/// <summary>Maps the custom-property and optional-interface entities used by relational scenarios.</summary>
public sealed class TreeContext : DbContext
{
    /// <summary>Creates an integration-test context using the configured relational provider.</summary>
    /// <param name="options">The provider and interceptor configuration.</param>
    public TreeContext(
        DbContextOptions<TreeContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        if (Database.IsNpgsql())
        {
            // WHY: A nondefault schema verifies that internal lock SQL honors relational table mapping.
            modelBuilder.HasDefaultSchema("hierarchies");
        }

        var node = modelBuilder.Entity<TreeNode>();
        node.HasKey(x => x.NodeId);
        node
            .Property(x => x.NodeId)
            .ValueGeneratedNever();
        node.HasNestedSet(x => x
            .HasNodeKey(n => n.NodeId)
            .HasBounds(n => n.Start, n => n.End)
            .HasDepth(n => n.Depth)
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.Parent)
            .HasPosition(n => n.Position));
        var concurrent = modelBuilder.Entity<ConcurrentNode>();
        concurrent
            .Property(x => x.Id)
            .ValueGeneratedNever();
        concurrent.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));
        var unscoped = modelBuilder.Entity<UnscopedQueryNode>();
        unscoped
            .Property(x => x.Id)
            .ValueGeneratedNever();
        unscoped.HasQueryFilter(node => node.Visible);
        unscoped.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(node => node.Id)
            .HasTreeId(node => node.TreeId)
            .HasParent(node => node.ParentId)
            .HasBounds(node => node.Left, node => node.Right)
            .HasDepth(node => node.Depth)
            .HasPosition(node => node.Position));
        modelBuilder
            .Entity<UnrelatedRow>()
            .Property(x => x.Id)
            .ValueGeneratedNever();
        modelBuilder.Entity<GeneratedAuditRow>();
        var binary = modelBuilder.Entity<BinaryNode>();
        binary
            .Property(x => x.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();
        binary
            .Property(x => x.ParentId)
            .HasMaxLength(64);
        binary.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));
        var constrained = modelBuilder.Entity<ConstrainedNode>();
        constrained
            .Property(x => x.Id)
            .ValueGeneratedNever();
        constrained
            .HasOne<ConstrainedNode>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.Tree,
                x.ParentId
            })
            .HasPrincipalKey(x => new
            {
                x.Tree,
                x.Id
            })
            .OnDelete(DeleteBehavior.Restrict);
        constrained.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));
        var guid = modelBuilder.Entity<GuidNode>();
        guid
            .Property(x => x.Id)
            .ValueGeneratedNever();
        guid.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));
        var text = modelBuilder.Entity<TextNode>();
        text
            .Property(x => x.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();
        text
            .Property(x => x.ParentId)
            .HasMaxLength(64);
        text
            .Property(x => x.Tree)
            .HasMaxLength(64);
        if (Database.IsSqlite())
        {
            // WHY: Deliberately leave ParentId binary: lookups must use the principal key collation.
            text
                .Property(x => x.Id)
                .UseCollation("NOCASE");
        }

        text.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));
        if (Database.IsNpgsql())
        {
            // WHY: Lowercase lock columns exercise mapped names instead of relying on CLR-name SQL.
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(property.Name.ToLowerInvariant());
                }
            }
        }
    }
}

/// <summary>An entity with custom names for keys, scope, parent and nested-set boundaries.</summary>
public class TreeNode
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets application data that structural operations must not select or materialize.</summary>
    public string? Payload { get; set; }

    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public int NodeId { get; set; }

    /// <summary>Gets or sets the left interval boundary.</summary>
    public long Start { get; set; }

    /// <summary>Gets or sets the right interval boundary.</summary>
    public long End { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Tree { get; set; }

    /// <summary>Gets or sets the direct parent key, or null for a root.</summary>
    public int? Parent { get; set; }

    /// <summary>Gets or sets the zero-based depth from the forest root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the zero-based position among siblings.</summary>
    public long Position { get; set; }
}

/// <summary>An optional-interface entity with a Guid key and nullable Guid parent.</summary>
public sealed class GuidNode : IScopedNestedSetNode<Guid, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Tree { get; set; }

    int IScopedNestedSetNode<Guid, Guid, int>.Scope => Tree;

    /// <summary>Gets or sets the direct parent key, or null for a root.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the left interval boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right interval boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth from the forest root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the zero-based position among siblings.</summary>
    public long Position { get; set; }
}

/// <summary>An optional-interface entity used to exercise string-key collation.</summary>
public sealed class TextNode : IScopedNestedSetNode<string, Guid, string>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public string Tree { get; set; } = "";

    string IScopedNestedSetNode<string, Guid, string>.Scope => Tree;

    /// <summary>Gets or sets the direct parent key, or null for a root.</summary>
    public string? ParentId { get; set; }

    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public string Id { get; set; } = "";

    /// <summary>Gets or sets the left interval boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right interval boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth from the forest root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the zero-based position among siblings.</summary>
    public long Position { get; set; }
}

/// <summary>An optional-interface entity with a restrictive self-referencing foreign key.</summary>
public sealed class ConstrainedNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Tree { get; set; }

    int IScopedNestedSetNode<int, Guid, int>.Scope => Tree;

    /// <summary>Gets or sets the direct parent key, or null for a root.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the left interval boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right interval boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth from the forest root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the zero-based position among siblings.</summary>
    public long Position { get; set; }
}
