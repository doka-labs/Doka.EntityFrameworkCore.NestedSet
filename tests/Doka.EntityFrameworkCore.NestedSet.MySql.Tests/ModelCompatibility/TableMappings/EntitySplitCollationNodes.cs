namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Stores payload in one table and native string hierarchy identities in a secondary fragment.</summary>
internal sealed class EntitySplitCollationNode
{
    /// <summary>Gets or sets the assigned node key repeated in both table fragments.</summary>
    internal string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the Scope persisted only in the structural fragment.</summary>
    internal string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable tree identity within the Scope.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the optional parent key in the structural fragment.</summary>
    internal string? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application metadata in the primary payload fragment.</summary>
    internal string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets additional application payload in the primary fragment.</summary>
    internal string Payload { get; set; } = string.Empty;
}
