namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>A string scope whose aliases follow an explicit case-insensitive database collation.</summary>
public sealed class CompiledScope
{
    /// <summary>Gets or sets the persistent application-owned anchor key.</summary>
    public string Id { get; set; } = string.Empty;
}
