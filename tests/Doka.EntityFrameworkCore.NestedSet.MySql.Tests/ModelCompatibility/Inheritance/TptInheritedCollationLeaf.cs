namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Declares the original TPT mapping owner's table collation and separately stored payload.</summary>
internal sealed class TptInheritedCollationLeaf : TptInheritedCollationNode
{
    /// <summary>Gets or sets leaf payload that must survive another Scope's root insertion.</summary>
    internal string Payload { get; set; } = string.Empty;
}
