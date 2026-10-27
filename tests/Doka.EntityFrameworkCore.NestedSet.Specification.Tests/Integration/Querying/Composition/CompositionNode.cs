namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides neutral payload and structural state for query-composition contracts.</summary>
internal sealed class CompositionNode
{
    /// <summary>Gets or sets the application scope, including equal node keys in different scopes.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the scope-local node key.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent key.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left coordinate.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right coordinate.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the stored structural depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application visibility independently of the stored structure.</summary>
    internal bool Visible { get; set; } = true;

    /// <summary>Gets or sets whether the incoming direct edge is allowed by the application.</summary>
    internal bool EdgeOpen { get; set; } = true;

    /// <summary>Gets or sets whether this node is an eligible owner.</summary>
    internal bool Candidate { get; set; }

    /// <summary>Gets or sets the selected owner's independently tested payload.</summary>
    internal bool Matches { get; set; }

    /// <summary>Gets or sets the inclusive start of the incoming edge's application validity.</summary>
    internal DateOnly FromDate { get; set; } = new(2044, 10, 1);

    /// <summary>Gets or sets the inclusive end of incoming edge validity, or an unbounded end.</summary>
    internal DateOnly? ToDate { get; set; }
}
