namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>A physical foreign key read from a provider catalog.</summary>
/// <param name="Name">The physical constraint name or SQLite identifier.</param>
/// <param name="DependentColumns">The dependent columns in key order.</param>
/// <param name="PrincipalColumns">The principal columns in key order.</param>
/// <param name="DeleteAction">The provider-reported delete action.</param>
public sealed record MigrationForeignKey(
    string Name,
    IReadOnlyList<string> DependentColumns,
    IReadOnlyList<string> PrincipalColumns,
    string DeleteAction
);
