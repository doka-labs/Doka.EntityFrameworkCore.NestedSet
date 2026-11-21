namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents application work that must share the ordering save's transaction.</summary>
public sealed class OrderingMarker
{
    /// <summary>Gets or sets the application identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the application payload.</summary>
    public string Value { get; set; } = "";
}
