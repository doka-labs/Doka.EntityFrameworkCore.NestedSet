namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Selects a metadata owner independently of the predicate applied to its values.</summary>
internal enum TagOwnerMode
{
    /// <summary>Accepts any visible matching ancestor or the node itself.</summary>
    Any,

    /// <summary>Checks only the structurally nearest owner of the requested classification.</summary>
    Nearest,
}
