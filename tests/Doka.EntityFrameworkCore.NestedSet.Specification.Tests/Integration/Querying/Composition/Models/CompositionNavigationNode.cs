namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Combines scoped hierarchy identity with owned and ordinary navigation payloads.</summary>
internal sealed class CompositionNavigationNode
{
    /// <summary>Gets or sets the scope component of the principal and dependent identities.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the assigned scope-local node key.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the complete hierarchy's stable identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the nullable scope-local parent key.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left coordinate.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right coordinate.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the stored structural depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets whether the hierarchy query filter exposes this node.</summary>
    internal bool Visible { get; set; } = true;

    /// <summary>Gets or sets the required reference owned by this principal.</summary>
    internal CompositionNavigationDetail Details { get; set; } = new();

    /// <summary>Gets the automatically included owned collection.</summary>
    internal List<CompositionNavigationTag> Tags { get; } = [];

    /// <summary>Gets the ordinary related collection projected explicitly by the application.</summary>
    internal List<CompositionNavigationItem> Items { get; } = [];
}
