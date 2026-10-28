namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Defines one permitted group metadata field.</summary>
public sealed class GroupMetadataDefinition
{
    /// <summary>Gets or sets the authorized tenant identity.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the catalog field code.</summary>
    public string FieldCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the supported scalar kind.</summary>
    public string ValueKind { get; set; } = "Text";

    /// <summary>Gets or sets the group type owning this field definition.</summary>
    public int GroupTypeId { get; set; }
}
