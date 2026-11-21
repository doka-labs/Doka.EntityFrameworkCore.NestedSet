namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides ordinary mapped roles whose CLR identity does not identify the selected named mapping.</summary>
internal sealed class CompositionGuardNode
{
    /// <summary>Gets or sets the node key, which can have the same value in every named mapping.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the optional scope configured only on the scoped named mapping.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the direct parent key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the left structural boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right structural boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the zero-based structural depth.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    public long Position { get; set; }
}
