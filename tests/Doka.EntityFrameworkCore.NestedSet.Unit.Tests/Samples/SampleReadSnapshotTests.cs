extern alias UserGroupsSample;

using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Checks the actual shared sample binary's snapshot and transaction ownership contract.</summary>
public sealed partial class SampleReadSnapshotTests
{
    /// <summary>Every qualified provider has one explicit default for a complete multi-statement read view.</summary>
    /// <param name="provider">The exact EF provider name.</param>
    /// <param name="expected">The required owned isolation.</param>
    [Theory]
    [InlineData("Doka.EntityFrameworkCore.MySql", IsolationLevel.RepeatableRead)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.RepeatableRead)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.Snapshot)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.Serializable)]
    public void OwnedIsolationMatchesProviderContract(
        string provider,
        IsolationLevel expected
    )
    {
        // Arrange: The theory supplies the provider and its required isolation.

        // Act
        var actual = SampleReadSnapshot.RequiredIsolation(provider);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Qualified caller isolation retains one shared view across participating reads.</summary>
    /// <param name="provider">The exact EF provider name.</param>
    /// <param name="actual">The isolation reported by the driver's active transaction.</param>
    [Theory]
    [InlineData("Doka.EntityFrameworkCore.MySql", IsolationLevel.RepeatableRead)]
    [InlineData("Doka.EntityFrameworkCore.MySql", IsolationLevel.Snapshot)]
    [InlineData("Doka.EntityFrameworkCore.MySql", IsolationLevel.Serializable)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.RepeatableRead)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.Snapshot)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.Serializable)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.Snapshot)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.Serializable)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.Serializable)]
    public void AdequateCallerIsolationIsAccepted(
        string provider,
        IsolationLevel actual
    )
    {
        // Arrange: The theory supplies a qualified provider/isolation pair.

        // Act
        var error = Record.Exception(() => SampleReadSnapshot.RequireIsolation(provider, actual));

        // Assert
        Assert.Null(error);
    }

    /// <summary>Weaker isolation cannot silently downgrade a complete report or chain snapshot.</summary>
    /// <param name="provider">The exact EF provider name.</param>
    /// <param name="actual">The inadequate transaction isolation.</param>
    [Theory]
    [InlineData("Doka.EntityFrameworkCore.MySql", IsolationLevel.Unspecified)]
    [InlineData("Doka.EntityFrameworkCore.MySql", IsolationLevel.Chaos)]
    [InlineData("Doka.EntityFrameworkCore.MySql", IsolationLevel.ReadUncommitted)]
    [InlineData("Doka.EntityFrameworkCore.MySql", IsolationLevel.ReadCommitted)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.Unspecified)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.Chaos)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.ReadUncommitted)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", IsolationLevel.ReadCommitted)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.Unspecified)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.Chaos)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.ReadUncommitted)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.ReadCommitted)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", IsolationLevel.RepeatableRead)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.Unspecified)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.Chaos)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.ReadUncommitted)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.ReadCommitted)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.RepeatableRead)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", IsolationLevel.Snapshot)]
    public void InadequateCallerIsolationIsRejected(
        string provider,
        IsolationLevel actual
    )
    {
        // Arrange: The theory supplies an explicitly inadequate provider/isolation pair.

        // Act
        var error = Record.Exception(() => SampleReadSnapshot.RequireIsolation(provider, actual));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Unknown providers cannot borrow another engine's default or bypass qualification.</summary>
    /// <param name="provider">The unknown provider name, including absent configuration.</param>
    /// <param name="caller">Whether caller isolation rather than the owned default is checked.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("Other.Relational.Provider", false)]
    [InlineData("Other.Relational.Provider", true)]
    public void UnknownProvidersAreRejected(
        string? provider,
        bool caller
    )
    {
        // Arrange: The theory supplies an unknown provider and the policy entry point.

        // Act
        var error = Record.Exception(() =>
        {
            if (caller)
            {
                SampleReadSnapshot.RequireIsolation(provider, IsolationLevel.Serializable);
            }
            else
            {
                _ = SampleReadSnapshot.RequiredIsolation(provider);
            }
        });

        // Assert
        Assert.IsType<NotSupportedException>(error);
    }
}
