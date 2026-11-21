namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Identifies a structural violation and the node whose stored data requires attention.</summary>
/// <typeparam name="TKey">The mapped node primary key type.</typeparam>
/// <param name="Code">The stable, machine-readable violation category.</param>
/// <param name="NodeKey">The offending node's primary key in the selected scope.</param>
/// <param name="Message">A human-readable explanation without application payload or scope values.</param>
internal sealed record NestedSetValidationIssue<TKey>(
    NestedSetValidationCode Code,
    TKey NodeKey,
    string Message
)
    where TKey : notnull;
