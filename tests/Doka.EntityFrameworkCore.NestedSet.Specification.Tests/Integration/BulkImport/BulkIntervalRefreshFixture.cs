namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns unordered generated-key tables with computed concurrency and complex scalar values.</summary>
public sealed class BulkIntervalRefreshFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Returns a cleared generated-value context on the selected real relational engine.</summary>
    public async Task<BulkIntervalRefreshContext> ResetAsync(
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
        var options = new DbContextOptionsBuilder<BulkIntervalRefreshContext>()
            .ConfigureTestWarnings()
            .UseNestedSets();

        foreach (var extension in source.GetService<IDbContextOptions>()
                     .Extensions)
        {
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(extension);
        }

        await using (var setup = new BulkIntervalRefreshContext(options.Options))
        {
            if (create)
            {
                await setup
                    .GetService<IRelationalDatabaseCreator>()
                    .CreateTablesAsync(CancellationToken.None);
            }

            await setup
                .Set<BulkIntervalNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    CancellationToken.None);

            await setup
                .Set<BulkIntervalNode>()
                .ExecuteDeleteAsync(CancellationToken.None);

            await setup
                .Set<BulkStageTextNode>()
                .ExecuteDeleteAsync(CancellationToken.None);

            await setup
                .Set<BulkStageBinaryParent>()
                .ExecuteDeleteAsync(CancellationToken.None);

            // WHY: Fresh import cases must not inherit active identities or tombstones from a previous case.
            await NestedSetTestInfrastructure.ClearRegistriesAsync(setup, CancellationToken.None);
        }

        return new BulkIntervalRefreshContext(options.AddInterceptors(interceptors).Options);
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

/// <summary>Maps update-generated values that must be selected after unresolved parents are assigned.</summary>
public sealed class BulkIntervalRefreshContext : DbContext
{
    /// <summary>Creates an independent context using the fixture's provider and command observers.</summary>
    public BulkIntervalRefreshContext(
        DbContextOptions<BulkIntervalRefreshContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<BulkIntervalNode>();
        node.ToTable(nameof(BulkIntervalNode));
        node
            .Property(value => value.Name)
            .HasMaxLength(80)
            .HasDefaultValue("Generated");

        var sql = this.GetService<ISqlGenerationHelper>();
        var sum = $"COALESCE({sql.DelimitIdentifier(nameof(BulkIntervalNode.ParentId))}, 0)"
            + $" + {sql.DelimitIdentifier(nameof(BulkIntervalNode.Depth))}"
            + $" + {sql.DelimitIdentifier(nameof(BulkIntervalNode.Position))}";

        var expression = Database.IsSqlServer() ? $"CAST({sum} AS int)" : sum;

        // WHY: Computed values change after parent repair on every provider, independently of trigger timing.
        node
            .Property(value => value.Version)
            .HasComputedColumnSql(expression, stored: true)
            .IsConcurrencyToken();

        var details = node.ComplexProperty(value => value.Details);
        details
            .Property(value => value.Label)
            .HasMaxLength(80)
            .HasDefaultValue("Generated detail");
        details
            .Property(value => value.Revision)
            .HasComputedColumnSql(expression + " + 1000", stored: true);
        node.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));

        var collation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsNpgsql()
                ? "C"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_BIN2"
                    : "utf8mb4_bin";

        var text = modelBuilder.Entity<BulkStageTextNode>();
        text.ToTable(nameof(BulkStageTextNode));
        var key = text
            .Property(value => value.Id)
            .HasMaxLength(64)
            .UseCollation(collation)
            .ValueGeneratedNever();

        // WHY: SQL distinguishes C/c while application equality deliberately does not. Refresh must verify the
        // exact inserted key instead of allowing a same-count replacement through the application's comparer.
        key.Metadata.SetValueComparer(
            new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<string>(
                (
                    left,
                    right
                ) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                value => StringComparer.OrdinalIgnoreCase.GetHashCode(value),
                value => value));


        text
            .Property(value => value.Scope)
            .HasMaxLength(64)
            .UseCollation(collation);
        text
            .Property(value => value.ParentId)
            .HasMaxLength(64)
            .UseCollation(collation);
        text.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));

        var binary = modelBuilder.Entity<BulkStageBinaryParent>();
        binary.ToTable(nameof(BulkStageBinaryParent));
        binary
            .Property(value => value.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();
        binary
            .Property(value => value.ParentId)
            .HasMaxLength(64);
        binary.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));
    }
}

/// <summary>Exercises identity generation, ordinary payload retention and final computed-value refresh.</summary>
public sealed class BulkIntervalNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the independent forest scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the immediate parent identity.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the default generated during insertion.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets ordinary application data that the final scalar refresh must not project.</summary>
    public string? Payload { get; set; }

    /// <summary>Gets or sets the computed optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets complex payload containing default and update-generated values.</summary>
    public BulkGeneratedDetails Details { get; set; } = new();

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
