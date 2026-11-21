namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>An immutable view of all persisted values in one hierarchy row.</summary>
/// <param name="NodeId">The primary key.</param>
/// <param name="Scope">The application scope.</param>
/// <param name="TreeId">The stable tree identity, or zero before the historical upgrade.</param>
/// <param name="ParentId">The parent identifier.</param>
/// <param name="Left">The left boundary.</param>
/// <param name="Right">The right boundary.</param>
/// <param name="Depth">The depth below the root.</param>
/// <param name="Position">The sibling position.</param>
/// <param name="Payload">The application payload.</param>
/// <param name="Category">The application-owned secondary sibling-order value.</param>
public sealed record MigrationNodeValue(
    int NodeId,
    int Scope,
    int TreeId,
    int? ParentId,
    long Left,
    long Right,
    int Depth,
    long Position,
    string Payload,
    int Category
);
