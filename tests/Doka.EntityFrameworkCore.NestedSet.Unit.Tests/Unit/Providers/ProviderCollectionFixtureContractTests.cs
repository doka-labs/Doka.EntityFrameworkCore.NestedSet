namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards the entire collection fixture set independently of resource initialization and discovery.</summary>
public sealed class ProviderCollectionFixtureContractTests
{
    /// <summary>Every executable registers exactly its owned, engine-bound model database fixtures.</summary>
    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void OwnedCollectionRegistersExactlyItsEngineFixtures(
        string project
    )
    {
        // Arrange
        var definition = CreateDefinition(ExpectedRegistrations(project));
        var assemblyName = AssemblyName(project);

        // Act
        var errors = ProviderCollectionFixtureContract.FindErrors(definition, assemblyName);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Bare, foreign, wrong-resource, and unrelated registrations cannot hide behind a filtered subset.
    /// </summary>
    [Theory]
    [InlineData(typeof(ModelCompatibilityDatabase))]
    [InlineData(typeof(ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine>))]
    [InlineData(typeof(ProviderFixture<ProviderResources, SqliteEngine>))]
    [InlineData(typeof(UnrelatedFixture))]
    public void UnexpectedCollectionFixtureIsRejectedWithoutActivation(
        Type unexpected
    )
    {
        // Arrange
        var definition = CreateDefinition([.. ExpectedRegistrations("Sqlite"), unexpected]);
        var assemblyName = AssemblyName("Sqlite");

        // Act
        var errors = ProviderCollectionFixtureContract.FindErrors(definition, assemblyName);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("unexpected collection fixture", error, StringComparison.Ordinal);
        Assert.Contains(unexpected.ToString(), error, StringComparison.Ordinal);
    }

    /// <summary>Neither MySQL nor MariaDB may disappear from their shared executable's collection.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingMySqlFamilyFixtureIsRejected(
        int omitted
    )
    {
        // Arrange
        var expected = ExpectedRegistrations("MySql");
        var definition = CreateDefinition(expected.Where((_, index) => index != omitted).ToArray());
        var assemblyName = AssemblyName("MySql");

        // Act
        var errors = ProviderCollectionFixtureContract.FindErrors(definition, assemblyName);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("missing collection fixture", error, StringComparison.Ordinal);
        Assert.Contains(
            expected[omitted].ToString(),
            error,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Inherited collection registrations belong to the complete set, including unexpected resources.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedRegistrationsRemainInExactSet(
        bool unexpected
    )
    {
        // Arrange
        var registrations = ExpectedRegistrations("Sqlite");
        var parent = CreateDefinition(
            unexpected ? [.. registrations, typeof(ModelCompatibilityDatabase)] : registrations);

        var definition = CreateDefinition([], parent);
        var assemblyName = AssemblyName("Sqlite");

        // Act
        var errors = ProviderCollectionFixtureContract.FindErrors(definition, assemblyName);

        // Assert
        Assert.Equal(unexpected ? 1 : 0, errors.Count);
        Assert.All(errors, error => Assert.Contains("unexpected collection fixture", error, StringComparison.Ordinal));
    }

    /// <summary>An empty registration set reports every missing fixture rather than vacuously passing.</summary>
    [Theory]
    [InlineData("MySql", 2)]
    [InlineData("PostgreSql", 1)]
    [InlineData("SqlServer", 1)]
    [InlineData("Sqlite", 1)]
    public void EmptyDefinitionRejectsEveryExpectedFixture(
        string project,
        int expectedCount
    )
    {
        // Arrange
        var definition = CreateDefinition([]);
        var assemblyName = AssemblyName(project);

        // Act
        var errors = ProviderCollectionFixtureContract.FindErrors(definition, assemblyName);

        // Assert
        Assert.Equal(expectedCount, errors.Count);
        Assert.All(errors, error => Assert.Contains("missing collection fixture", error, StringComparison.Ordinal));
    }

    /// <summary>A missing collection definition is rejected before its registration metadata is inspected.</summary>
    [Fact]
    public void NullDefinitionIsRejected()
    {
        // Arrange
        var assemblyName = AssemblyName("Sqlite");

        // Act
        var error = Record.Exception(() => ProviderCollectionFixtureContract.FindErrors(null!, assemblyName));

        // Assert
        Assert.IsType<ArgumentNullException>(error);
    }

    /// <summary>An unregistered executable cannot select a permissive empty fixture contract.</summary>
    [Fact]
    public void UnknownAssemblyIsRejected()
    {
        // Arrange
        var definition = CreateDefinition([]);

        // Act
        var error = Record.Exception(() => ProviderCollectionFixtureContract.FindErrors(definition, "Unknown.Tests"));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Unknown.Tests", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Creates real inherited interface metadata without activating any registered fixture.</summary>
    private static Type CreateDefinition(
        Type[] registrations,
        Type? parent = null
    )
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("CollectionRegistrationProbe"),
            AssemblyBuilderAccess.RunAndCollect);

        var module = assembly.DefineDynamicModule("CollectionRegistrationProbe");
        var definition = module.DefineType("CollectionDefinition", TypeAttributes.Public, parent);

        foreach (var fixture in registrations)
        {
            definition.AddInterfaceImplementation(typeof(ICollectionFixture<>).MakeGenericType(fixture));
        }

        return definition.CreateType();
    }

    /// <summary>Provides an independent expected registration set instead of reusing the guard's catalog.</summary>
    private static Type[] ExpectedRegistrations(
        string project
    ) => project switch
    {
        "MySql" =>
        [
            typeof(ProviderFixture<ModelCompatibilityDatabase, MySqlEngine>),
            typeof(ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine>),
        ],
        "PostgreSql" => [typeof(ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine>)],
        "SqlServer" => [typeof(ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine>)],
        "Sqlite" => [typeof(ProviderFixture<ModelCompatibilityDatabase, SqliteEngine>)],
        _ => throw new InvalidOperationException($"Unknown collection registration probe '{project}'."),
    };

    /// <summary>Gets the executable identity expected by the metadata-only ownership guard.</summary>
    private static string AssemblyName(
        string project
    ) => $"Doka.EntityFrameworkCore.NestedSet.{project}.Tests";

    /// <summary>
    /// Fails immediately if a metadata check accidentally constructs an unrelated registered resource.
    /// </summary>
    public sealed class UnrelatedFixture
    {
        /// <summary>Demonstrates that a registration-set audit must not initialize fixture resources.</summary>
        public UnrelatedFixture()
        {
            throw new InvalidOperationException("The metadata guard must not activate fixture resources.");
        }
    }
}
