using Doka.NestedSet;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Stores independent Scope and TreeId strings without adding a parent domain entity.</summary>
public sealed class UnicodeIdentityCompiledNode : INestedSetNode<int, string>
{
    /// <summary>Gets or sets the independent application partition.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional direct parent identifier.</summary>
    public int? ParentId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <inheritdoc />
    public string TreeId { get; set; } = string.Empty;

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
