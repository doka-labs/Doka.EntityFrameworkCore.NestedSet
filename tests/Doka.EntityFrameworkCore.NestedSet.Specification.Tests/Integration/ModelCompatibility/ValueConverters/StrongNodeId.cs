namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies a hierarchy node without exposing its provider integer representation.</summary>
/// <param name="Value">The stable provider value.</param>
internal readonly record struct StrongNodeId(int Value);
