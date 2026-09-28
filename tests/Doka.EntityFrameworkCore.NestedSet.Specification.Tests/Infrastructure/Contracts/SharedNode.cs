namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares a physical table with a hierarchy entity to exercise the unsupported-mapping guard.</summary>
public sealed class SharedNode
{
    /// <summary>Gets or sets the primary key and identifying foreign key for table sharing.</summary>
    public int Id { get; set; }
}
