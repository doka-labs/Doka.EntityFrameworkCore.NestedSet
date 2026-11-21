namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a scoped hierarchy with a composite EF primary key and a scalar alternate NodeKey.</summary>
internal sealed class CompositeKeyNode
{
    /// <summary>Gets or sets the tenant component of both relational identities.</summary>
    internal int TenantId { get; set; }

    /// <summary>Gets or sets the row-local primary-key component.</summary>
    internal int RowId { get; set; }

    /// <summary>Gets or sets the stable scalar hierarchy identity.</summary>
    internal int NodeKey { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent NodeKey.</summary>
    internal int? ParentNodeKey { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application payload.</summary>
    internal string Name { get; set; } = "";
}
