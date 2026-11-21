namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents optional payload sharing the hierarchy principal's physical table row.</summary>
internal sealed class TableSplitPayload
{
    /// <summary>Gets or sets the identifying shared primary key.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets payload stored outside the hierarchy aggregate.</summary>
    internal string? Description { get; set; }
}
