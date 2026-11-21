namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Contains payload stored as an EF owned entity in the node table.</summary>
internal sealed class OwnedNodeDetails
{
    /// <summary>Gets or sets payload text.</summary>
    internal string Label { get; set; } = "";
}
