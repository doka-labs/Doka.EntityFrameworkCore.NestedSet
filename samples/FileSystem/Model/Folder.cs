namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Represents one folder in an independently identified, unscoped tree.</summary>
/// <remarks>Structural properties are maintained by NestedSet; application code edits the folder payload.</remarks>
public sealed class Folder : INestedSetNode<int, Guid>
{
    /// <summary>Creates a detached folder ready for an explicit hierarchy insertion.</summary>
    /// <param name="name">The displayed name and primary sibling-order criterion.</param>
    /// <param name="category">The application-owned category used by ancestor queries and secondary ordering.</param>
    public Folder(
        string name,
        string category = "User"
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Name = name;
        Category = category;
    }

    /// <summary>Gets the database-generated identity used by anchor queries.</summary>
    public int Id { get; private set; }

    /// <summary>Gets the stable identity that isolates this folder's tree.</summary>
    public Guid TreeId { get; private set; }

    /// <summary>Gets or sets the name; a tracked rename is reordered by SaveChangesAsync.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the application-owned category.</summary>
    public string Category { get; set; }

    /// <summary>Gets the direct parent identity, or null for this tree's root.</summary>
    public int? ParentId { get; private set; }

    /// <summary>Gets the inclusive left boundary within this tree.</summary>
    public long Left { get; private set; }

    /// <summary>Gets the inclusive right boundary within this tree.</summary>
    public long Right { get; private set; }

    /// <summary>Gets the number of ancestors; the root has depth zero.</summary>
    public int Depth { get; private set; }

    /// <summary>Gets the zero-based position among this folder's siblings.</summary>
    public long Position { get; private set; }
}
