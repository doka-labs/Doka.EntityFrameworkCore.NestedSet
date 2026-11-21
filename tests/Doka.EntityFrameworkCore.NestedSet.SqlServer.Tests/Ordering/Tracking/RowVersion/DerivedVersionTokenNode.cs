namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Owns a native SQL Server rowversion absent from the configured base owner.</summary>
public sealed class DerivedVersionTokenNode : DerivedVersionBaseNode
{
    /// <summary>Gets or sets the generated token changed by every structural UPDATE.</summary>
    public byte[] Version { get; set; } = [];
}
