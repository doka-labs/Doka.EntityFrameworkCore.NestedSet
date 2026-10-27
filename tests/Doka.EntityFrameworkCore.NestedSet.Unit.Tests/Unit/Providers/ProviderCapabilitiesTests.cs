namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies concrete adapter policies and lock dialects independently of database availability.</summary>
public sealed partial class ProviderCapabilitiesTests
{
    /// <summary>Each selected adapter owns its explicit provider identity and locking isolation.</summary>
    /// <param name="engine">The concrete adapter under test.</param>
    /// <param name="name">The exact native provider identity.</param>
    /// <param name="isolation">The minimum write isolation required by its lock protocol.</param>
    [Theory]
    [InlineData("Sqlite", "Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.Serializable)]
    [InlineData("MySql", "Doka.EntityFrameworkCore.MySql", IsolationLevel.ReadCommitted)]
    [InlineData("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.ReadCommitted)]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.ReadCommitted)]
    public void SelectedAdaptersOwnTheirProviderAndRequiredIsolation(
        string engine,
        string name,
        IsolationLevel isolation
    )
    {
        // Arrange
        var provider = NestedSetTestAdapters.ForEngine(engine);

        // Act
        var providerName = provider.ProviderName;
        var requiredIsolation = provider.RequiredIsolation;

        // Assert
        Assert.Equal(name, providerName);
        Assert.Equal(isolation, requiredIsolation);
    }
}
