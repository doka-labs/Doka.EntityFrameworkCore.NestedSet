namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns native case-sensitive and binary scope tables for managed import guard regressions.</summary>
public sealed class BulkStageGuardFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Resets guard tables without restarting a shared fixture-owned database engine.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    public async Task<BulkStageGuardContext> ResetAsync(
        string engine,
        CancellationToken cancellationToken
    )
    {
        var create = !_databases.TryGetValue(engine, out var database);

        if (create)
        {
            database = await TestDatabase.CreateAsync(engine, cancellationToken: cancellationToken);
            _databases.Add(engine, database);
        }

        await using var source = database!.CreateContext();
        var extensions = source
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptions<DbContext>(extensions);
        var context = new BulkStageGuardContext(options);

        if (create)
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);
        }

        await context
            .Set<BulkStageTextNode>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (string?)null),
                cancellationToken);

        await context
            .Set<BulkStageTextNode>()
            .ExecuteDeleteAsync(cancellationToken);

        await context
            .Set<BulkStageBinaryParent>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (byte[]?)null),
                cancellationToken);

        await context
            .Set<BulkStageBinaryParent>()
            .ExecuteDeleteAsync(cancellationToken);

        await context
            .Set<BulkStageBinaryScope>()
            .ExecuteDeleteAsync(cancellationToken);

        // WHY: Fresh import cases must not inherit active identities or tombstones from a previous case.
        await NestedSetTestInfrastructure.ClearRegistriesAsync(context, cancellationToken);

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

/// <summary>Allows application comparers broader than native string equality without adding relationships.</summary>
public sealed class BulkStageGuardContext : DbContext
{
    /// <summary>Uses the fixture-owned provider configuration.</summary>
    public BulkStageGuardContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var collation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsNpgsql()
                ? "C"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_BIN2"
                    : "utf8mb4_bin";

        var text = modelBuilder.Entity<BulkStageTextNode>();
        var key = text
            .Property(node => node.Id)
            .HasMaxLength(64)
            .UseCollation(collation)
            .ValueGeneratedNever();

        // WHY: Providers may use case-insensitive string-key tracking independently of the binary SQL collation.
        // The fixture needs distinct A/a principals while only Parent and Scope deliberately use broader equality.
        key.Metadata.SetValueComparer(
            ValueComparer.CreateDefault<string>(
                favorStructuralComparisons: true));

        var scope = text
            .Property(node => node.Scope)
            .HasMaxLength(64)
            .UseCollation(collation);

        var parent = text
            .Property(node => node.ParentId)
            .HasMaxLength(64)
            .UseCollation(collation);

        var comparer = new ValueComparer<string?>(
            (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            value => value == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value),
            value => value);

        // WHY: Only guard properties use broader CLR equality; the principal primary key stays ordinal.
        scope.Metadata.SetValueComparer(comparer);
        parent.Metadata.SetValueComparer(comparer);
        text.HasNestedSet(builder => builder
            .HasTreeId(node => node.TreeId)
            .HasScope(node => node.Scope)
            .HasParent(node => node.ParentId));

        var binaryParent = modelBuilder.Entity<BulkStageBinaryParent>();
        binaryParent
            .Property(node => node.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();
        binaryParent
            .Property(node => node.ParentId)
            .HasMaxLength(64);
        binaryParent.HasNestedSet(builder => builder
            .HasTreeId(node => node.TreeId)
            .HasScope(node => node.Scope)
            .HasParent(node => node.ParentId));

        var binaryScope = modelBuilder.Entity<BulkStageBinaryScope>();
        binaryScope
            .Property(node => node.Id)
            .ValueGeneratedNever();
        binaryScope
            .Property(node => node.Scope)
            .HasMaxLength(64)
            .Metadata
            .SetValueComparer(
                ValueComparer.CreateDefault<byte[]>(
                    favorStructuralComparisons: true));
        binaryScope.HasNestedSet(builder => builder
            .HasTreeId(node => node.TreeId)
            .HasScope(node => node.Scope)
            .HasParent(node => node.ParentId));
    }
}
