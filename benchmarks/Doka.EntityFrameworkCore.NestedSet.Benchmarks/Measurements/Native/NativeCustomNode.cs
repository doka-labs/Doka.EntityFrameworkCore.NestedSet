namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>An entity with custom names for keys, scope, parent and nested-set boundaries.</summary>
public class NativeCustomNode
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets application data that structural operations must not select or materialize.</summary>
    public string? Payload { get; set; }

    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public int NodeId { get; set; }

    /// <summary>Gets or sets the left interval boundary.</summary>
    public long Start { get; set; }

    /// <summary>Gets or sets the right interval boundary.</summary>
    public long End { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Tree { get; set; }

    /// <summary>Gets or sets the direct parent key, or null for a root.</summary>
    public int? Parent { get; set; }

    /// <summary>Gets or sets the zero-based depth from the forest root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the zero-based position among siblings.</summary>
    public long Position { get; set; }
}
