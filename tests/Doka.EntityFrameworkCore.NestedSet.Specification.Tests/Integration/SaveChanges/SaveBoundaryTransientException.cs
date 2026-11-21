namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Identifies the deterministic transient fault injected before a payload command reaches the server.
/// </summary>
public sealed class SaveBoundaryTransientException : Exception;
