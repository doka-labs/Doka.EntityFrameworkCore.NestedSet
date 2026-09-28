namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies provider identities and first-use lock dialects independently of database availability.</summary>
public sealed class ProviderCapabilitiesTests
{
    /// <summary>Verifies only explicit provider identities can select a supported locking contract.</summary>
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", "Sqlite", IsolationLevel.Serializable)]
    [InlineData("Doka.EntityFrameworkCore.MySql", "MySql", IsolationLevel.ReadCommitted)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", "PostgreSql", IsolationLevel.ReadCommitted)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "SqlServer", IsolationLevel.ReadCommitted)]
    public void ExactProviderNamesSelectRequiredIsolation(
        string name,
        string kind,
        IsolationLevel isolation
    )
    {
        // Arrange
        var providerName = name;

        // Act
        var provider = Providers.NestedSetProviderCapabilities.Resolve(providerName);

        // Assert
        Assert.Equal(kind, provider.Kind.ToString());
        Assert.Equal(isolation, provider.RequiredIsolation);
    }

    /// <summary>Verifies matching suffixes or near-matching names never opt an unverified provider into writes.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Unknown.EntityFrameworkCore.MySql")]
    [InlineData("doka.entityframeworkcore.mysql")]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory")]
    public void UnverifiedProviderIdentitiesAreRejected(
        string? name
    )
    {
        // Arrange
        var providerName = name;

        // Act
        var error = Assert.Throws<NotSupportedException>(() =>
            Providers.NestedSetProviderCapabilities.Resolve(providerName));

        // Assert
        Assert.Contains("Doka MySQL", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies MySQL's changing upsert accepts both insertion and duplicate-update row counts.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void MySqlLockWritesUseItsDocumentedAffectedRowContract(
        int affected,
        bool expected
    )
    {
        // Arrange
        var provider = Providers.NestedSetProviderCapabilities.Resolve("Doka.EntityFrameworkCore.MySql");

        // Act
        var accepted = provider.IsLockWriteCount(affected);

        // Assert
        Assert.Equal(expected, accepted);
    }

    /// <summary>Verifies SQL Server serializes absent infrastructure keys and uses row locks under snapshot reads.</summary>
    [Fact]
    public void SqlServerLocksProtectAbsentAndPresentKeys()
    {
        // Arrange
        var provider = Providers.NestedSetProviderCapabilities.Resolve("Microsoft.EntityFrameworkCore.SqlServer");

        // Act
        var insert = provider.InfrastructureLockSql("[a].[Locks]", "[Id]", "[Revision]");
        var anchor = provider.AnchorLockSql("[a].[Anchors]", "[Id]", "[Value]");

        // Assert
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", insert, StringComparison.Ordinal);
        Assert.Contains("WHERE NOT EXISTS", insert, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK, ROWLOCK)", anchor, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR UPDATE", anchor, StringComparison.Ordinal);
    }
}
