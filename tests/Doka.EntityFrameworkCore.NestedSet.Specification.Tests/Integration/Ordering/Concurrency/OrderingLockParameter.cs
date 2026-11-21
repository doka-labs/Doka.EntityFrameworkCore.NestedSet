namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves a provider parameter's value and relational data type.</summary>
/// <param name="Value">The copied scalar or binary value.</param>
/// <param name="Type">The provider-reported relational type.</param>
internal sealed record OrderingLockParameter(
    object? Value,
    DbType Type
);
