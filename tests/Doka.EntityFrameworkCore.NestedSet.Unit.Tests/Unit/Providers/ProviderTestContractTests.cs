namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that raw metadata preserves fixture ownership and effective shared engine coverage.</summary>
public sealed partial class ProviderTestContractTests
{
    /// <summary>The local audit leaves shared-base declarations to the separate shared coverage guard.</summary>
    [Fact]
    public async Task InheritedSharedRowsAreNotAuditedAsLocalDeclarations()
    {
        // Arrange
        var name = new AssemblyName("InheritedOwnershipProbe");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name.Name!);
        var builder = module.DefineType(
            "InheritedOwnershipProbe",
            TypeAttributes.Public,
            typeof(ProviderFixtureIsolationTests.FixtureContextProbe));

        var suite = builder.CreateType();

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("PostgreSql"));

        // Assert
        Assert.Contains(
            suite.GetMethods(),
            method => method.DeclaringType == typeof(ProviderFixtureIsolationTests.FixtureContextProbe));

        Assert.Empty(errors);
    }

    /// <summary>MySQL and MariaDB share one executable while the remaining projects each own one engine.</summary>
    [Theory]
    [InlineData("MySql", 2)]
    [InlineData("PostgreSql", 1)]
    [InlineData("SqlServer", 1)]
    [InlineData("Sqlite", 1)]
    public void ExecutableOwnershipIncludesEveryEngine(
        string project,
        int count
    )
    {
        // Arrange
        var assemblyName = AssemblyName(project);

        // Act
        var engines = ProviderTestContract.OwnedEngines(assemblyName);

        // Assert
        Assert.Equal(count, engines.Length);
        Assert.Contains(project, engines);
    }

    /// <summary>Ordinary local facts use the leaf's exact fixture rather than explicit provider annotations.</summary>
    [Fact]
    public async Task OrdinaryLocalFactUsesConcreteFixture()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Both Doka-family markers are accepted in the same provider executable.</summary>
    [Theory]
    [InlineData(typeof(MySqlEngine))]
    [InlineData(typeof(MariaDbEngine))]
    public async Task OrdinaryFamilyFactUsesItsLeafMarker(
        Type marker
    )
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()], project: "MySql", markers: [marker], inherited: true);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("MySql"));

        // Assert
        Assert.Empty(errors);
        Assert.True(ProviderTestContract.HasLocalTestCases(suite));
        Assert.False(ProviderTestContract.HasDeclaredTestCases(suite));
    }

    /// <summary>Provider-looking ordinary payloads are scenario data and cannot select a database.</summary>
    [Theory]
    [InlineData("provider")]
    [InlineData("foreignEngine")]
    public async Task OrdinaryLocalTheoryAllowsEngineLookingPayload(
        string parameterName
    )
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralTheory(), NeutralInline(["PostgreSql"])], parameterName: parameterName);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>An ordinary local theory cannot accept a selector that contradicts fixture ownership.</summary>
    [Fact]
    public async Task OrdinaryLocalEngineSelectorIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralTheory(), NeutralInline(["Sqlite"])], parameterName: "engine");

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("not an engine argument", StringComparison.Ordinal));
    }

    /// <summary>Raw local member data preserves an explicitly configured external source type.</summary>
    [Fact]
    public async Task OrdinaryLocalExternalMemberSourceIsPreserved()
    {
        // Arrange
        var suite = CreateNeutralProbe(
            [NeutralTheory(), NeutralMember(nameof(RawSources.PayloadRows))],
            parameterName: "payload");

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Unset MemberType resolves through the concrete leaf to its local base's public factory.</summary>
    [Fact]
    public async Task OrdinaryInheritedMemberSourceUsesReflectedLeaf()
    {
        // Arrange
        var suite = CreateNeutralProbe(
            [NeutralTheory(), NeutralMember("Rows", explicitSource: false)],
            parameterName: "payload",
            inherited: true,
            localRows: true);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Empty(errors);
        Assert.Equal(suite, suite.GetMethod("Probe")!.ReflectedType);
    }

    /// <summary>An empty ordinary factory inherited from a local base remains a visible failure.</summary>
    [Fact]
    public async Task OrdinaryInheritedEmptyMemberSourceIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe(
            [NeutralTheory(), NeutralMember("Rows", explicitSource: false)],
            parameterName: "payload",
            inherited: true,
            localRows: true,
            emptyRows: true);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("is empty", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("no raw data rows", StringComparison.Ordinal));
    }

    /// <summary>Invalid metadata inherited from a local family base cannot evade the concrete-leaf audit.</summary>
    [Fact]
    public async Task OrdinaryInheritedUnknownExclusionIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact(["Postgres"])], inherited: true);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("unknown excluded provider 'Postgres'", StringComparison.Ordinal));
    }

    /// <summary>Fixture ownership is checked even when method exclusions would hide the local declaration.</summary>
    [Fact]
    public async Task ExcludedLocalMethodCannotHideForeignFixture()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact(["SqlServer"])], markers: [typeof(SqlServerEngine)]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("foreign provider project", StringComparison.Ordinal));
    }

    /// <summary>The owning executable passed to the guard cannot disagree with the actual concrete leaf.</summary>
    [Fact]
    public async Task OrdinaryLocalFixtureRejectsForeignAuditOwner()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("PostgreSql"));

        // Assert
        Assert.Contains(errors, error => error.Contains("provider 'Sqlite' is foreign", StringComparison.Ordinal));
    }

    /// <summary>An ordinary local suite without a concrete marker cannot infer an owner from its payload.</summary>
    [Fact]
    public async Task OrdinaryLocalMissingFixtureIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()], markers: []);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("must inject a concrete provider fixture", StringComparison.Ordinal));
    }

    /// <summary>Exact closed fixture markers may not contradict one another in a single concrete leaf.</summary>
    [Fact]
    public async Task OrdinaryLocalContradictoryFixturesAreRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe(
            [NeutralFact()],
            project: "MySql",
            markers: [typeof(MySqlEngine), typeof(MariaDbEngine)]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("MySql"));

        // Assert
        Assert.Contains(errors, error => error.Contains("contradictory provider fixtures", StringComparison.Ordinal));
    }

    /// <summary>An injected fixture is insufficient when xUnit cannot resolve its exact registration.</summary>
    [Fact]
    public async Task OrdinaryLocalUnregisteredFixtureIsRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralFact()], registerFixture: false);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("unresolved test constructor argument", StringComparison.Ordinal));
    }

    /// <summary>Ordinary local declarations must expose their immutable fixture engine through ProviderTest.</summary>
    [Fact]
    public async Task OrdinaryLocalMissingProviderBaseIsRejected()
    {
        // Arrange
        var suite = CreateProbe([NeutralFact()]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("must derive from ProviderTest", StringComparison.Ordinal));
    }

    /// <summary>A local ordinary theory without any raw rows remains a test failure.</summary>
    [Fact]
    public async Task OrdinaryLocalMissingRowsAreRejected()
    {
        // Arrange
        var suite = CreateNeutralProbe([NeutralTheory()]);

        // Act
        var errors = await ProviderTestContract.FindDeclaredOwnershipErrorsAsync(suite, AssemblyName("Sqlite"));

        // Assert
        Assert.Contains(errors, error => error.Contains("no raw data rows", StringComparison.Ordinal));
    }
}
