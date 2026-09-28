namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns independent ordering tables while reusing the established real-engine provisioning.</summary>
public sealed class OrderingFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Returns an empty strict or flexible model without modifying another test fixture.</summary>
    /// <param name="engine">The real relational engine.</param>
    /// <param name="mode">Strict, Flexible, or Converted.</param>
    /// <returns>A context owned by the test.</returns>
    public async Task<OrderingContext> ResetAsync(
        string engine,
        string mode = "Strict"
    )
    {
        if (!_databases.TryGetValue(engine, out var database))
        {
            database = await TestDatabase.CreateAsync(engine);
            _databases.Add(engine, database);

            foreach (var model in new[] { "Strict", "Flexible", "Converted" })
            {
                await using var setup = await CreateContextAsync(engine, model);
                await setup
                    .GetService<IRelationalDatabaseCreator>()
                    .CreateTablesAsync(CancellationToken.None);
            }
        }

        var context = await CreateContextAsync(engine, mode);
        await context
            .Set<OrderingNode>()
            .Where(node => node.ParentId != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (int?)null),
                CancellationToken.None);

        // WHY: The production model intentionally uses a restrictive self-FK. Unlinking the fixture rows first keeps
        // cleanup valid after an interrupted mutation leaves a populated hierarchy behind.
        await context
            .Set<OrderingNode>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<OrderingMarker>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await NestedSetTestInfrastructure.ClearRegistriesAsync(context, CancellationToken.None);

        return context;
    }

    /// <summary>Creates an independent context with optional fault observers against an initialized model.</summary>
    /// <param name="engine">The real relational engine.</param>
    /// <param name="mode">The mapped ordering contract.</param>
    /// <param name="interceptors">Observers owned by the calling test.</param>
    /// <returns>A context owned by the caller.</returns>
    public async Task<OrderingContext> CreateContextAsync(
        string engine,
        string mode = "Strict",
        params IInterceptor[] interceptors
    )
    {
        await using var source = _databases[engine]
            .CreateContext();
        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder().ConfigureTestWarnings();

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
        var observers = interceptors
            .OfType<OrderingSaveProbe>()
            .ToArray();

        if (observers.Length != 0)
        {
            // WHY: Only actual entity-materialization assertions need the singleton observer and its service graph.
            options.AddInterceptors(TestMaterializationObserver.Instance);
        }

        OrderingContext context = mode switch
        {
            "Strict" => new StrictOrderingContext(options.Options),
            "Flexible" => new FlexibleOrderingContext(options.Options),
            "Converted" => new ConvertedOrderingContext(options.Options),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        foreach (var observer in observers)
        {
            TestMaterializationObserver.Instance.Register(context, observer.RecordMaterialization);
        }

        return context;
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

/// <summary>Maps ordering-specific domain data through the ordinary asynchronous save integration.</summary>
public abstract class OrderingContext : NestedSetDbContext
{
    /// <summary>Creates a context using the provider selected by its fixture.</summary>
    protected OrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the distinct prefix preventing cross-model table and lock collisions.</summary>
    protected abstract string TablePrefix { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => ConfigureModel(modelBuilder, TablePrefix);

    /// <summary>Shares the exact table mapping with the deliberately unintegrated negative-control context.</summary>
    internal static void ConfigureModel(
        ModelBuilder modelBuilder,
        string tablePrefix
    )
    {
        var node = modelBuilder.Entity<OrderingNode>();
        node.ToTable(tablePrefix + "OrderingNodes");
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();
        node
            .Property(value => value.Name)
            .HasMaxLength(128);
        node
            .Property(value => value.Payload)
            .HasMaxLength(256);
        node
            .Property(value => value.Category)
            .HasConversion<string>()
            .HasMaxLength(32);
        node.HasNestedSet(builder =>
        {
            builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId);

            if (tablePrefix == "Converted")
            {
                // WHY: Alphabetic provider values differ from enum ordinals and expose CLR-side ordering.
                builder
                    .OrderBy(value => value.Category)
                    .ThenBy(value => value.Name, NullSortOrder.Last);
            }
            else
            {
                builder
                    .OrderBy(value => value.Name, NullSortOrder.Last)
                    .ThenByDescending(value => value.Priority);
            }

            if (tablePrefix == "Flexible")
            {
                builder.HasOrderMode(NestedSetOrderMode.AllowManualPlacement);
            }
        });

        var marker = modelBuilder.Entity<OrderingMarker>();
        marker.ToTable(tablePrefix + "OrderingMarkers");
        marker
            .Property(value => value.Id)
            .ValueGeneratedNever();
        marker
            .Property(value => value.Value)
            .HasMaxLength(128);
    }
}

/// <summary>Uses the default strict ordering mode and an independent EF model cache entry.</summary>
public sealed class StrictOrderingContext : OrderingContext
{
    /// <summary>Creates the strict test context.</summary>
    public StrictOrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string TablePrefix => "Strict";
}

/// <summary>Allows explicit placement while retaining automatic placement for ordinary inserts and renames.</summary>
public sealed class FlexibleOrderingContext : OrderingContext
{
    /// <summary>Creates the flexible test context.</summary>
    public FlexibleOrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string TablePrefix => "Flexible";
}

/// <summary>Orders a converted domain value through its native persisted representation.</summary>
public sealed class ConvertedOrderingContext : OrderingContext
{
    /// <summary>Creates the converted-sort test context.</summary>
    public ConvertedOrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string TablePrefix => "Converted";
}

/// <summary>Omits SaveChanges integration while mapping the same sorted hierarchy as the strict model.</summary>
public sealed class MissingIntegrationOrderingContext : DbContext
{
    /// <summary>Creates a negative-control context with the selected real provider.</summary>
    public MissingIntegrationOrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => OrderingContext.ConfigureModel(modelBuilder, "Strict");
}

/// <summary>Provides an order whose enum ordinals intentionally disagree with the converted text order.</summary>
public enum OrderingCategory
{
    /// <summary>The alphabetically last category with the smallest ordinal.</summary>
    Zeta,

    /// <summary>The alphabetically first category.</summary>
    Alpha,

    /// <summary>The middle category.</summary>
    Beta,
}

/// <summary>Stores sorted domain values separately from maintained hierarchy coordinates.</summary>
public sealed class OrderingNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the independent forest.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the optional direct parent.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the nullable ascending domain sort value; null values sort last.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the descending secondary sort value.</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets ordinary data that does not participate in ordering.</summary>
    public string Payload { get; set; } = "original";

    /// <summary>Gets or sets the converted domain order used by the converted model.</summary>
    public OrderingCategory Category { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}

/// <summary>Represents application work that must share the ordering save's transaction.</summary>
public sealed class OrderingMarker
{
    /// <summary>Gets or sets the application identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the application payload.</summary>
    public string Value { get; set; } = "";
}
