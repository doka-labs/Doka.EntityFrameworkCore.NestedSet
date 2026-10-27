namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Provides the small, explicitly bounded hierarchy used by native Unicode identity checks.</summary>
/// <typeparam name="TIdentity">The model type of both registry identity roles.</typeparam>
internal sealed class UnicodeIdentityNode<TIdentity>
    where TIdentity : notnull
{
    /// <summary>Gets or sets the independent assigned node identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the optional application partition.</summary>
    internal TIdentity Scope { get; set; } = default!;

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal TIdentity TreeId { get; set; } = default!;

    /// <summary>Gets or sets the direct parent identity.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets an ordinary payload changed through the coordinated save boundary.</summary>
    internal string Name { get; set; } = "Initial";
}

/// <summary>Distinguishes model identities from their converted relational text representation.</summary>
/// <param name="Value">The provider text retained without normalization.</param>
internal readonly record struct UnicodeIdentityValue(string Value);
