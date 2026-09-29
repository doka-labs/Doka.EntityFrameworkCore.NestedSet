namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Represents an employee in a tenant-owned supervisor hierarchy.</summary>
public sealed class User
{
    /// <summary>Gets or sets the generated employee key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the tenant shared by the supervisor tree and group membership.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the direct supervisor key; null identifies the top-level manager.</summary>
    public int? SupervisorId { get; set; }

    /// <summary>Gets or sets the employee's displayed name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the employee's business position, independently of tree ordering.</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of supervisors above this employee.</summary>
    public int Depth { get; set; }
}
