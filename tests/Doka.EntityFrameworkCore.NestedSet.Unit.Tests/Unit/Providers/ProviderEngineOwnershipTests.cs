namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that shared integration cases are assigned to exactly one provider project.</summary>
public sealed class ProviderEngineOwnershipTests
{
    /// <summary>Each integration assembly discovers only cases owned by its database provider.</summary>
    [Theory]
    [InlineData("MySql", "MySql", true)]
    [InlineData("MySql", "MariaDb", true)]
    [InlineData("MySql", "PostgreSql", false)]
    [InlineData("PostgreSql", "PostgreSql", true)]
    [InlineData("PostgreSql", "SqlServer", false)]
    [InlineData("SqlServer", "SqlServer", true)]
    [InlineData("SqlServer", "Sqlite", false)]
    [InlineData("Sqlite", "Sqlite", true)]
    [InlineData("Sqlite", "MySql", false)]
    public void SelectionMatchesOwningProvider(
        string project,
        string engine,
        bool expected
    )
    {
        // Arrange
        var assembly = $"Doka.EntityFrameworkCore.NestedSet.{project}.Tests";

        // Act
        var included = ProviderEngineOwnership.Includes(engine, assembly);

        // Assert
        Assert.Equal(expected, included);
    }

    /// <summary>An unregistered test project cannot silently execute integration cases.</summary>
    [Fact]
    public void UnknownAssemblyIsRejected()
    {
        // Arrange
        const string assembly = "Doka.EntityFrameworkCore.NestedSet.Unknown.Tests";

        // Act
        var error = Record.Exception(() => ProviderEngineOwnership.Includes("Sqlite", assembly));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>The specification library cannot become a runtime provider merely by defining the selector.</summary>
    [Fact]
    public void SharedSpecificationAssemblyIsRejected()
    {
        // Arrange
        var assembly = typeof(ProviderEngineOwnership).Assembly.GetName().Name!;

        // Act
        var error = Record.Exception(() => ProviderEngineOwnership.Includes("Sqlite", assembly));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(assembly, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Runtime selection follows each current xUnit assembly without retaining a previous provider.</summary>
    [Fact]
    public async Task RuntimeSelectionUsesCurrentAssemblyIdentity()
    {
        // Arrange
        var sqlite = CreateTestAssembly("Sqlite");
        var mysql = CreateTestAssembly("MySql");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        // WHY: The child execution context lets the probe change xUnit state without replacing its own test context.
        var selections = await Task.Run(
            () =>
            {
                TestContext.SetForTestAssembly(sqlite, TestEngineStatus.Running, cancellationToken);
                var first = ProviderEngineOwnership.Includes("Sqlite");
                TestContext.SetForTestAssembly(mysql, TestEngineStatus.Running, cancellationToken);
                var previous = ProviderEngineOwnership.Includes("Sqlite");
                var current = ProviderEngineOwnership.Includes("MariaDb");

                return (first, previous, current);
            },
            cancellationToken);

        // Assert
        Assert.Contains(", Version=", sqlite.AssemblyName, StringComparison.Ordinal);
        Assert.Equal((true, false, true), selections);
    }

    /// <summary>A misspelled engine fails before it can be excluded from every provider project.</summary>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void UnknownEngineIsRejectedBeforeFiltering(
        string project
    )
    {
        // Arrange
        const string engine = "Postgres";
        var assembly = $"Doka.EntityFrameworkCore.NestedSet.{project}.Tests";

        // Act
        var error = Record.Exception(() => ProviderEngineOwnership.Includes(engine, assembly));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Postgres", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The canonical entries retain each built-in marker's identity and exact executable owner.</summary>
    [Theory]
    [InlineData("MySql", typeof(MySqlEngine), "MySql")]
    [InlineData("MariaDb", typeof(MariaDbEngine), "MySql")]
    [InlineData("PostgreSql", typeof(PostgreSqlEngine), "PostgreSql")]
    [InlineData("SqlServer", typeof(SqlServerEngine), "SqlServer")]
    [InlineData("Sqlite", typeof(SqliteEngine), "Sqlite")]
    public void CatalogEntriesMatchBuiltinMarkers(
        string engine,
        Type marker,
        string project
    )
    {
        // Arrange
        var assemblyName = $"Doka.EntityFrameworkCore.NestedSet.{project}.Tests";

        // Act
        var definition = ProviderEngineOwnership.Engines.Single(entry => entry.Name == engine);

        // Assert
        Assert.Equal(marker, definition.MarkerType);
        Assert.Equal(assemblyName, definition.AssemblyName);
        Assert.Equal(engine, marker.GetProperty(nameof(IProviderEngine.Name))!.GetValue(null));
    }

    /// <summary>A consumer cannot alter canonical ownership through a mutable collection interface.</summary>
    [Fact]
    public void CatalogDoesNotExposeMutableEntries()
    {
        // Arrange
        var entries = ProviderEngineOwnership.Engines;
        var original = entries[0];
        var foreign = new ProviderEngineDefinition("Postgres", typeof(PostgreSqlEngine), "Unknown.Tests");

        // Act
        var error = Record.Exception(() =>
        {
            if (entries is IList<ProviderEngineDefinition> mutable)
            {
                mutable[0] = foreign;
            }
        });

        // Assert
        Assert.True(error is null or NotSupportedException);
        Assert.Same(original, entries[0]);
    }

    /// <summary>Catalog membership accepts only exact built-in names, including no null or case aliases.</summary>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", true)]
    [InlineData(null, false)]
    [InlineData("mysql", false)]
    [InlineData("Postgres", false)]
    public void KnownEngineSelectionUsesExactNames(
        string? engine,
        bool expected
    )
    {
        // Arrange
        var candidate = engine;

        // Act
        var known = ProviderEngineOwnership.IsKnown(candidate);

        // Assert
        Assert.Equal(expected, known);
    }

    /// <summary>An unknown executable cannot appear to own an empty engine set.</summary>
    [Fact]
    public void UnknownAssemblyCannotAppearAsEmptyOwnership()
    {
        // Arrange
        const string assemblyName = "Doka.EntityFrameworkCore.NestedSet.Unknown.Tests";

        // Act
        var error = Record.Exception(() => ProviderEngineOwnership.OwnedBy(assemblyName));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(assemblyName, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Runtime ownership fails visibly when no current xUnit test assembly is available.</summary>
    [Fact]
    public async Task RuntimeSelectionRejectsMissingAssembly()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        Task<Exception?> probe;

        // Act
        // WHY: A fresh execution context removes ambient xUnit metadata without replacing this test's own context.
        using (ExecutionContext.SuppressFlow())
        {
            probe = Task.Run(
                () => Record.Exception(() => ProviderEngineOwnership.Includes("Sqlite")),
                cancellationToken);
        }

        var error = await probe;

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("No current integration test assembly", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Creates an xUnit assembly with the same full identity shape as a real provider project.</summary>
    private static XunitTestAssembly CreateTestAssembly(
        string project
    )
    {
        var name = new AssemblyName($"Doka.EntityFrameworkCore.NestedSet.{project}.Tests");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);

        return new XunitTestAssembly(assembly, null, assemblyPath: string.Empty);
    }
}
