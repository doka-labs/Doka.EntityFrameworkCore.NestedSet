namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Stores complex scalar defaults and trigger values in the hierarchy table.</summary>
public sealed class BulkGeneratedDetails
{
    /// <summary>Gets or sets the database-generated default inside the complex payload.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets the SQLite trigger-generated value changed by structural bulk updates.</summary>
    public int Revision { get; set; }
}
