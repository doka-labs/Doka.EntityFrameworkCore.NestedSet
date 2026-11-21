namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TreeRegistryTableNameTests
{
    /// <summary>Applies a test-specific model configuration.</summary>
    private sealed class ConfiguredContext : DbContext
    {
        private readonly Action<ModelBuilder> _configure;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ConfiguredContext(
            DbContextOptions<ConfiguredContext> options,
            Action<ModelBuilder> configure
        ) : base(options)
        {
            _configure = configure;
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => _configure(modelBuilder);
    }

    /// <summary>Provides the mapped hierarchy shape.</summary>
    private abstract class Node
    {
        /// <summary>Gets or sets the stable identity.</summary>
        public long Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        public long? ParentId { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the distance from the root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Provides a first hierarchy.</summary>
    private sealed class FirstNode : Node;

    /// <summary>Provides a second hierarchy.</summary>
    private sealed class SecondNode : Node;

    /// <summary>Provides a third hierarchy.</summary>
    private sealed class ThirdNode : Node;

    /// <summary>Provides a base hierarchy.</summary>
    private class BaseNode : Node;

    /// <summary>Provides a derived hierarchy type.</summary>
    private sealed class DerivedNode : BaseNode;

    /// <summary>Provides an application table.</summary>
    private sealed class Order
    {
        /// <summary>Gets or sets the identity.</summary>
        public long Id { get; set; }
    }
}
