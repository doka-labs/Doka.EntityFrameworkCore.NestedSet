namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents application audit data whose key the database generates during persistence.</summary>
public sealed class GeneratedAuditRow
{
    /// <summary>Gets or sets the store-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the audited operation description.</summary>
    public string Value { get; set; } = "";
}
