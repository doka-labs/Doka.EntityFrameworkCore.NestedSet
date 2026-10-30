namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Retains selected entity aliases in the documented simple MemberInit projection form.</summary>
internal sealed class CompositionSemanticRow
{
    /// <summary>Gets the explicitly selected mapped endpoint.</summary>
    public required CompositionNode Source { get; init; }

    /// <summary>Gets the independent mapped endpoint retained by the same row.</summary>
    public required CompositionNode Other { get; init; }

    /// <summary>Gets the independent application payload carried through a joined row.</summary>
    public int Value { get; init; }
}
