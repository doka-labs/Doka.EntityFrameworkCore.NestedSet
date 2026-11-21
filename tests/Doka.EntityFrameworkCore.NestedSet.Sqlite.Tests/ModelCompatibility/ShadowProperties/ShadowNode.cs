namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Represents payload whose entire hierarchy structure is stored in EF shadow properties.</summary>
internal sealed class ShadowNode
{
    /// <summary>Gets or sets the application-assigned row and NodeKey identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets application payload.</summary>
    internal string Name { get; set; } = "";
}
