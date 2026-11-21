namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a folder in a TPT hierarchy.</summary>
internal sealed class TptFolderNode : InheritanceNode
{
    /// <summary>Gets or sets folder-specific payload.</summary>
    internal string FolderKind { get; set; } = "";
}
