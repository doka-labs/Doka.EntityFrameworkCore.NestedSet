namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Provides the original assigned-key, scoped, custom-bound hierarchy shape for disconnected measurements.
/// </summary>
internal static class TrackingHierarchy
{
    /// <summary>Installs every role explicitly so the benchmark does not depend on CLR-name conventions.</summary>
    internal static void Configure(
        ModelBuilder builder
    )
    {
        var node = builder.Entity<Node>();
        node.HasKey(value => value.NodeId);
        node.Property(value => value.NodeId).ValueGeneratedNever();
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(value => value.NodeId)
            .HasBounds(value => value.Start, value => value.End)
            .HasDepth(value => value.Depth)
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Tree)
            .HasParent(value => value.Parent)
            .HasPosition(value => value.Position));
    }

    /// <summary>Owns only the custom-bound hierarchy's real model and provider services.</summary>
    internal sealed class Context : TrackingMeasurementContext<Context>
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => Configure(modelBuilder);
    }

    /// <summary>Retains the same nine scalar roles and payload as the original wide-plan and guard workload.</summary>
    internal sealed class Node
    {
        /// <summary>Gets or sets the assigned node identity.</summary>
        public int NodeId { get; set; }

        /// <summary>Gets or sets the isolated scope.</summary>
        public int Tree { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets ordinary application payload.</summary>
        public string? Payload { get; set; }

        /// <summary>Gets or sets the lower interval endpoint.</summary>
        public long Start { get; set; }

        /// <summary>Gets or sets the upper interval endpoint.</summary>
        public long End { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        public int? Parent { get; set; }

        /// <summary>Gets or sets zero-based depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets sibling position.</summary>
        public long Position { get; set; }
    }
}
