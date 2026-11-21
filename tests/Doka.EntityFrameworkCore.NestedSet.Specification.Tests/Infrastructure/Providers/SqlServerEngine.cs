namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies SQL Server cases.</summary>
public readonly struct SqlServerEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "SqlServer";
}
