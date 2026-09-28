namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns native case-sensitive and binary scope tables for managed import guard regressions.</summary>
public sealed class BulkStageGuardFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Resets guard tables without restarting a shared fixture-owned database engine.</summary>
    public async Task<BulkStageGuardContext> ResetAsync(
        string engine
    )
    {
        var create = !_databases.TryGetValue(engine, out var database);

        if (create)
        {
            database = await TestDatabase.CreateAsync(engine);
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
                .CreateTablesAsync(CancellationToken.None);
        }

        await context
            .Set<BulkStageTextNode>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (string?)null),
                CancellationToken.None);

        await context
            .Set<BulkStageTextNode>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<BulkStageBinaryParent>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (byte[]?)null),
                CancellationToken.None);

        await context
            .Set<BulkStageBinaryParent>()
            .ExecuteDeleteAsync(CancellationToken.None);

        await context
            .Set<BulkStageBinaryScope>()
            .ExecuteDeleteAsync(CancellationToken.None);

        // WHY: Fresh import cases must not inherit active identities or tombstones from a previous case.
        await NestedSetTestInfrastructure.ClearRegistriesAsync(context, CancellationToken.None);

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
            Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer.CreateDefault<string>(
                favorStructuralComparisons: true));

        var scope = text
            .Property(node => node.Scope)
            .HasMaxLength(64)
            .UseCollation(collation);

        var parent = text
            .Property(node => node.ParentId)
            .HasMaxLength(64)
            .UseCollation(collation);

        var comparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<string?>(
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
                Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer.CreateDefault<byte[]>(
                    favorStructuralComparisons: true));
        binaryScope.HasNestedSet(builder => builder
            .HasTreeId(node => node.TreeId)
            .HasScope(node => node.Scope)
            .HasParent(node => node.ParentId));
    }
}
