namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a folder in a TPH hierarchy.</summary>
internal sealed class TphFolderNode : InheritanceNode
{
    /// <summary>Gets or sets folder-specific payload.</summary>
    internal string FolderKind { get; set; } = "";
}
