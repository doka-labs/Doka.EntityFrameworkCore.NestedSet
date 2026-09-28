namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshTests
{
    /// <summary>Exercises string key and scope aliases independently of the normal integer-key fixture.</summary>
    private sealed class RefreshScopeContext : NestedSetDbContext
    {
        /// <summary>Uses the shared engine connection and boundary-specific command observer.</summary>
        internal RefreshScopeContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureRefreshModel(modelBuilder, this);
    }

    /// <summary>Intentionally gives CLR scope equality broader semantics than the database scope column.</summary>
    private sealed class ScopeComparerContext : NestedSetDbContext
    {
        /// <summary>Uses separate tables on the existing fixture-owned engine.</summary>
        internal ScopeComparerContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigureRefreshModel(modelBuilder, this);
            modelBuilder
                .Entity<RefreshScopeNode>()
                .ToTable("ComparerScopeNodes");

            var collation = Database.IsSqlite()
                ? "BINARY"
                : Database.IsNpgsql()
                    ? "C"
                    : Database.IsSqlServer()
                        ? "Latin1_General_100_BIN2"
                        : "utf8mb4_bin";

            var scope = modelBuilder
                .Entity<RefreshScopeNode>()
                .Property(node => node.Scope)
                .UseCollation(collation);

            scope.Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<string>(
                    (
                        left,
                        right
                    ) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                    value => StringComparer.OrdinalIgnoreCase.GetHashCode(value),
                    value => value));
        }
    }

    /// <summary>Seeds a valid forest through ordinary EF outside the measured ordering save.</summary>
    private sealed class RefreshScopeSeedContext : DbContext
    {
        /// <summary>Uses the same mapped tables while bypassing only the save coordinator during arrangement.</summary>
        internal RefreshScopeSeedContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureRefreshModel(modelBuilder, this);
    }

    /// <summary>Declares native case-insensitive scope and key equality on every supported database engine.</summary>
    private static void ConfigureRefreshModel(
        ModelBuilder modelBuilder,
        DbContext context
    )
    {
        var postgresCollation = context is ScopeComparerContext
            ? "comparer_scope_case_insensitive"
            : "refresh_scope_case_insensitive";

        if (context.Database.IsNpgsql())
        {
            modelBuilder.HasCollation(
                postgresCollation,
                locale: "und-u-ks-level1",
                provider: "icu",
                deterministic: false);
        }

        var collation = context.Database.IsSqlite()
            ? "NOCASE"
            : context.Database.IsNpgsql()
                ? postgresCollation
                : context.Database.IsSqlServer()
                    ? "Latin1_General_100_CI_AI"
                    : "utf8mb4_general_ci";

        var entity = modelBuilder.Entity<RefreshScopeNode>();
        entity.ToTable("RefreshScopeNodes");
        entity
            .Property(node => node.Id)
            .HasMaxLength(80)
            .UseCollation(collation)
            .ValueGeneratedNever();
        entity
            .Property(node => node.Scope)
            .HasMaxLength(80)
            .UseCollation(collation);
        entity
            .Property(node => node.ParentId)
            .HasMaxLength(80)
            .UseCollation(collation);
        entity
            .Property(node => node.Name)
            .HasMaxLength(80);
        entity.HasNestedSet(builder => builder
            .HasTreeId(node => node.TreeId)
            .HasScope(node => node.Scope)
            .HasParent(node => node.ParentId)
            .OrderBy(node => node.Name));
    }

    /// <summary>Contains payload excluded from structural refresh plus database-collated string identities.</summary>
    private sealed class RefreshScopeNode : IScopedNestedSetNode<string, Guid, string>
    {
        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public string Id { get; set; } = "";

        /// <summary>Gets or sets the native-collated forest identity.</summary>
        public string Scope { get; set; } = "";

        /// <summary>Gets or sets the optional parent identity.</summary>
        public string? ParentId { get; set; }

        /// <summary>Gets or sets the configured sibling ordering value.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets unrelated application payload that refresh must neither select nor accept.</summary>
        public string Payload { get; set; } = "original payload";

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Captures commands without reading parameter values or retaining application entities.</summary>
    private sealed class RefreshScopeProbe : DbCommandInterceptor
    {
        /// <summary>Gets command templates executed during the measured save.</summary>
        internal List<string> Commands { get; } = [];

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }
    }
}
