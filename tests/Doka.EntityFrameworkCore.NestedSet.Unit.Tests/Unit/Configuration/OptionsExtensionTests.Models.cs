namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class OptionsExtensionTests
{
    /// <summary>Provides a context whose only nested-set bootstrap is the options extension.</summary>
    private sealed class EnabledContext : DbContext
    {
        /// <summary>Creates a context from externally configured options.</summary>
        /// <param name="options">The context options.</param>
        public EnabledContext(
            DbContextOptions<EnabledContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<Node>();
            entity.HasKey(node => new
            {
                node.Scope,
                node.RecordId,
            });

            entity.HasAlternateKey(node => new
            {
                node.Scope,
                node.NodeKey,
            });

            ConfigureNestedSet(entity);
        }
    }

    /// <summary>Provides an invalid model used to prove options-installed final validation.</summary>
    private sealed class InvalidContext : DbContext
    {
        /// <summary>Creates a context from externally configured options.</summary>
        /// <param name="options">The context options.</param>
        public InvalidContext(
            DbContextOptions<InvalidContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<Node>();
            entity.HasKey(node => new
            {
                node.RecordId,
                node.TreeId,
            });

            ConfigureNestedSet(entity);
        }
    }

    /// <summary>Provides a configured hierarchy without nested-set options registration.</summary>
    private sealed class PlainContext : DbContext
    {
        /// <summary>Creates a context from externally configured options.</summary>
        /// <param name="options">The context options.</param>
        public PlainContext(
            DbContextOptions<PlainContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<Node>();
            entity.HasKey(node => node.NodeKey);
            ConfigureNestedSet(entity);
        }
    }

    /// <summary>Supplies all structural properties used by the registered convention.</summary>
    private sealed class Node
    {
        /// <summary>Gets or sets the scope-local record identity.</summary>
        public int RecordId { get; set; }

        /// <summary>Gets or sets the stable scalar hierarchy identity.</summary>
        public Guid NodeKey { get; set; }

        /// <summary>Gets or sets the optional application partition.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the parent node identity.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>Gets or sets the first sibling order value.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the descending sibling order value.</summary>
        public int Kind { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the persisted depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }
}
