namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Stores an owned reference in its hierarchy principal's table.</summary>
internal sealed class CompositionNavigationDetail
{
    /// <summary>Gets or sets the reference payload that must survive subtree projection.</summary>
    internal string Caption { get; set; } = string.Empty;
}
