namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Combines assigned hierarchy identities with a row-distinct database-generated result.</summary>
internal sealed class AncestorIndexReadbackNode
{
    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the scope that isolates the tree.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public int TreeId { get; set; }

    /// <summary>Gets or sets the immediate parent identity.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the left interval boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right interval boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the depth below the root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets the independent input used to detect generated-result permutations.</summary>
    public int Input { get; set; }

    /// <summary>Gets or sets the generated result that must return to its own input entity.</summary>
    public long Stamp { get; set; }
}
