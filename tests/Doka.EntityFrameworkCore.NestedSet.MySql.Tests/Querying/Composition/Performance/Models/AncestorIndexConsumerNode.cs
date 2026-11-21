namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Maps only the scalar hierarchy coordinates used by the native regression.</summary>
internal sealed class AncestorIndexConsumerNode
{
    /// <summary>Gets or sets the globally unique node key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the fixture scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the tree identity.</summary>
    public int Tree { get; set; }

    /// <summary>Gets or sets the independently seeded direct parent.</summary>
    public int? Parent { get; set; }

    /// <summary>Gets or sets the opening traversal coordinate.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the closing traversal coordinate.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the independently seeded depth.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets the database-generated width classification.</summary>
    public bool IsNonLeaf { get; set; }
}
