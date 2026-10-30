namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Defines an application group classification independently of structural depth.</summary>
public sealed class GroupType
{
    /// <summary>Gets or sets the authorized tenant identity.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the application-assigned type identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the displayed type name.</summary>
    public string Name { get; set; } = string.Empty;
}
