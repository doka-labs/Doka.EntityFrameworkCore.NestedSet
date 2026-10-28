namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Stores one scalar value for one exact membership owner.</summary>
public sealed class MembershipMetadata
{
    /// <summary>Gets or sets the authorized tenant identity.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the user identity.</summary>
    public int UserId { get; set; }

    /// <summary>Gets or sets the group identity.</summary>
    public int UserGroupId { get; set; }

    /// <summary>Gets or sets the classified group type used to validate the field.</summary>
    public int GroupTypeId { get; set; }

    /// <summary>Gets or sets the defined field code.</summary>
    public string FieldCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the scalar routing value; null never matches.</summary>
    public string? Value { get; set; }
}
