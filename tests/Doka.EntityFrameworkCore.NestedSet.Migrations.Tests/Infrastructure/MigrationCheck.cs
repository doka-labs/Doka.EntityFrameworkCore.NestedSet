namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>A physical check constraint read from a provider catalog.</summary>
/// <param name="Name">The physical constraint name.</param>
/// <param name="Expression">The provider-rendered check expression.</param>
public sealed record MigrationCheck(
    string Name,
    string Expression
);
