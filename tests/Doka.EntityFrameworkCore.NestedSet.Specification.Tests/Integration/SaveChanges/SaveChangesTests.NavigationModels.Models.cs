namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Coordinates hierarchy work while preserving ordinary manually detected payload.</summary>
    private sealed class NavigationSaveContext : NestedSetDbContext
    {
        /// <summary>Creates the coordinated context from the established provider options.</summary>
        internal NavigationSaveContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureNavigationModel(modelBuilder);
    }

    /// <summary>Exercises provider registration without supplying a coordinated save override.</summary>
    private sealed class NavigationPlainContext : DbContext
    {
        /// <summary>Creates the ordinary context from the established provider options.</summary>
        internal NavigationPlainContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureNavigationModel(modelBuilder);
    }

    /// <summary>Contains hierarchy-owned scalars and the relationships whose snapshots are guarded.</summary>
    private sealed class NavigationNode : IScopedNestedSetNode<int, Guid, int>
    {
        /// <inheritdoc />
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the configured sibling order.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the direct parent without requiring an explicitly assigned FK.</summary>
        public NavigationNode? Parent { get; set; }

        /// <summary>Gets the collection whose EF relationship snapshot can retain detached entries.</summary>
        public List<NavigationNode> Children { get; } = [];

        /// <summary>Gets or sets the ordinary principal supplying the hierarchy scope key.</summary>
        public NavigationScopeOwner? ScopeOwner { get; set; }

        /// <summary>Gets or sets the ordinary nullable part of a foreign key that overlaps the guarded scope.</summary>
        public int? PayloadOwnerId { get; set; }

        /// <summary>Gets or sets an ordinary principal whose identity includes the existing hierarchy scope.</summary>
        public NavigationPayloadOwner? PayloadOwner { get; set; }

        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int Scope { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Provides a store-generated principal key and a cascade relationship to hierarchy nodes.</summary>
    private sealed class NavigationScopeOwner
    {
        /// <summary>Gets or sets the generated scope identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets ordinary principal payload.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Gets the hierarchy members in this application scope.</summary>
        public List<NavigationNode> Nodes { get; } = [];
    }

    /// <summary>Provides an ordinary relationship whose composite FK overlaps the hierarchy-owned scope.</summary>
    private sealed class NavigationPayloadOwner
    {
        /// <summary>Gets or sets the first component of the ordinary principal identity.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the explicit identity within the application scope.</summary>
        public int Id { get; set; }

        /// <summary>Gets the related nodes without making the ordinary relationship a hierarchy role.</summary>
        public List<NavigationNode> Nodes { get; } = [];
    }

    /// <summary>Represents ordinary application payload independent of the hierarchy.</summary>
    private sealed class NavigationNote
    {
        /// <summary>Gets or sets the explicit application key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the payload whose automatic detection policy must be retained.</summary>
        public string Value { get; set; } = "original";
    }

    /// <summary>Observes all writes to this navigation model without changing provider behavior.</summary>
    private sealed class NavigationWriteProbe : DbCommandInterceptor
    {
        /// <summary>Gets the number of application INSERT, UPDATE or DELETE commands.</summary>
        internal int Writes { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Observe(command);

            return result;
        }

        /// <inheritdoc />
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            Observe(command);

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Observe(command);

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
            Observe(command);

            return ValueTask.FromResult(result);
        }

        /// <summary>Counts model writes independently of provider-specific RETURNING clauses.</summary>
        private void Observe(
            DbCommand command
        )
        {
            if (command.CommandText.Contains("NavigationSave", StringComparison.Ordinal)
                && (command.CommandText.Contains("INSERT ", StringComparison.OrdinalIgnoreCase)
                    || command.CommandText.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
                    || command.CommandText.Contains("DELETE ", StringComparison.OrdinalIgnoreCase)))
            {
                Writes++;
            }
        }
    }
}
