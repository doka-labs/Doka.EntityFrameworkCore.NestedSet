namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>The historical and current models used to exercise a complete hierarchy-schema upgrade.</summary>
public enum MigrationStage
{
    /// <summary>The populated schema before Int64 coordinates and tree-scoped access paths were introduced.</summary>
    Baseline,

    /// <summary>The model configured through the current nested-set convention.</summary>
    Current,

    /// <summary>The current model with a user-owned filtered index and late relational name configuration.</summary>
    Specialized,
}
