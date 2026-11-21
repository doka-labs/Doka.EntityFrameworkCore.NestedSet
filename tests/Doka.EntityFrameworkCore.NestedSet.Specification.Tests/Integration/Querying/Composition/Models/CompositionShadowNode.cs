namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Keeps every structural role in the tracker while exposing deliberately unrelated CLR names.</summary>
internal sealed class CompositionShadowNode
{
    /// <summary>Gets or sets application payload independent of the structural mapping.</summary>
    internal string Label { get; set; } = string.Empty;

    /// <summary>Gets a misleading conventional name that must never replace the mapped shadow boundary.</summary>
    internal long Left { get; set; } = -100;

    /// <summary>Gets a misleading conventional name that must never replace the mapped shadow boundary.</summary>
    internal long Right { get; set; } = -99;

    /// <summary>Gets a misleading conventional name that must never replace the mapped shadow depth.</summary>
    internal int Depth { get; set; } = -10;
}
