using Doka.NestedSet;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Provides a hierarchy node for generated-model execution.</summary>
public sealed class TreeRegistryCompiledNode : INestedSetNode<int, Guid>
{
    /// <summary>Gets or sets the optional direct parent identifier.</summary>
    public int? ParentId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <inheritdoc />
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
