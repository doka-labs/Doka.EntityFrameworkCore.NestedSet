namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies MySQL cases within the Doka provider executable.</summary>
public readonly struct MySqlEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "MySql";
}
