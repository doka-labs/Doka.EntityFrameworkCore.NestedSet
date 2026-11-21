namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies MariaDB cases within the Doka provider executable.</summary>
public readonly struct MariaDbEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "MariaDb";
}
