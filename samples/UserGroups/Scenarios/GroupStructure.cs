namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Captures only the persisted hierarchy columns used by rejection and rollback comparisons.</summary>
/// <param name="Id">The group key.</param>
/// <param name="ParentId">The direct parent key.</param>
/// <param name="Left">The inclusive left boundary.</param>
/// <param name="Right">The inclusive right boundary.</param>
/// <param name="Depth">The maintained depth.</param>
/// <param name="Position">The maintained sibling position.</param>
internal readonly record struct GroupStructure(
    int Id,
    int? ParentId,
    long Left,
    long Right,
    int Depth,
    long Position
);
