namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Associates an operation value with its mapped property before provider parameter construction.</summary>
/// <param name="Name">The internal parameter name referenced by the SQL template.</param>
/// <param name="Property">The EF property supplying the exact relational type mapping.</param>
/// <param name="Value">The model value to convert through the mapping.</param>
internal readonly record struct NestedSetBatchParameter(
    string Name,
    string Property,
    object? Value
);
