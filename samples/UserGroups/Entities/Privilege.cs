namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Identifies a permission defined by the tenant's application policy.</summary>
public sealed class Privilege
{
    /// <summary>Gets or sets the tenant owning this permission definition.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the stable permission code inside the tenant.</summary>
    public string Code { get; set; } = string.Empty;
}
