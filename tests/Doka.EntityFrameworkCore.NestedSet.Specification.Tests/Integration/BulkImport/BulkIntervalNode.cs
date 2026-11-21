namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises identity generation, ordinary payload retention and final computed-value refresh.</summary>
public sealed class BulkIntervalNode : IScopedNestedSetNode<int, Guid, int>
{
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

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the independent forest scope.</summary>
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
