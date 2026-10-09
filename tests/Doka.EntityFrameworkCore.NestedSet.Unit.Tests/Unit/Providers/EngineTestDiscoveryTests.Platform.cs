namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class EngineTestDiscoveryTests
{
    /// <summary>Database applicability reads the concrete engine and requires an explicit metadata-only marker.</summary>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MariaDb", false)]
    [InlineData("PostgreSql", false)]
    [InlineData("SqlServer", false)]
    [InlineData("Sqlite", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", true)]
    public void PlatformHookUsesConcreteEngineWithoutActivatingResources(
        string engine,
        bool databaseIndependent
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, nameof(DiscoveryProbe.Ordinary), databaseIndependent: databaseIndependent);

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(method.TestClass.Class, method.Method);

        // Assert
        Assert.Equal(databaseIndependent ? null : engine, resolved);
    }

    /// <summary>An identity-only fixture is not evidence that a test body avoids the database.</summary>
    [Fact]
    public void ProviderResourcesDoesNotBypassSqlServerPlatformCheck()
    {
        // Arrange
        var method = CreateTestMethod("SqlServer", nameof(DiscoveryProbe.Ordinary),
            [typeof(ProviderFixture<ProviderResources, SqlServerEngine>)]);

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(method.TestClass.Class, method.Method);

        // Assert
        Assert.Equal("SqlServer", resolved);
    }

    /// <summary>The shared root registers xUnit's normal execution hook and metadata guards explicitly opt out.</summary>
    [Fact]
    public void SharedRootOwnsPlatformHookAndMetadataMarker()
    {
        // Arrange
        var root = typeof(ProviderTest);
        var metadata = typeof(ProviderAnnotationTests);

        // Act
        var hook = root.GetCustomAttribute<ProviderDatabasePlatformAttribute>();
        var independent = metadata.IsDefined(typeof(DatabaseIndependentAttribute), inherit: true);

        // Assert
        Assert.NotNull(hook);
        Assert.True(independent);
        Assert.IsAssignableFrom<BeforeAfterTestAttribute>(hook);
    }

    /// <summary>Database-free lifecycle methods run while the neighboring database isolation case stays guarded.</summary>
    [Theory]
    [InlineData(nameof(DatabaseLifecycleTests.SharedLibraryResolvesExecutingAssemblyFixture), true)]
    [InlineData(nameof(DatabaseLifecycleTests.AssemblyFixtureRejectsForeignServer), true)]
    [InlineData(nameof(DatabaseLifecycleTests.ServerDatabasesShareEngineWithoutSharingRows), false)]
    public void DatabaseIndependentMethodDoesNotDisableItsWholeSuite(
        string methodName,
        bool databaseIndependent
    )
    {
        // Arrange
        var suite = CreateTestMethod("SqlServer", nameof(DiscoveryProbe.Ordinary),
            [typeof(ProviderFixture<ProviderResources, SqlServerEngine>)]).TestClass.Class;
        var method = typeof(DatabaseLifecycleTests).GetMethod(methodName)!;

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(suite, method);

        // Assert
        Assert.Equal(databaseIndependent ? null : "SqlServer", resolved);
    }

    /// <summary>SQL Server compiled-model metadata remains available without starting a server.</summary>
    [Theory]
    [InlineData(nameof(CompiledModelTests.DesignModelUsesTypedTreeRegistries))]
    [InlineData(nameof(CompiledModelTests.CompiledRegistryNamesMatchDesignModel))]
    [InlineData(nameof(CompiledModelTests.CompiledStringIdentityCapturesMatchDesignModel))]
    public void CompiledModelMetadataDoesNotRequireSqlServer(
        string methodName
    )
    {
        // Arrange
        var suite = CreateTestMethod("SqlServer", nameof(DiscoveryProbe.Ordinary)).TestClass.Class;
        var method = typeof(CompiledModelTests).GetMethod(methodName)!;

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(suite, method);

        // Assert
        Assert.Null(resolved);
    }

    /// <summary>Connection-free binding, model, and value-comparer cases stay runnable on every local host.</summary>
    [Theory]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.ScopedHierarchyRequiresForScope))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.ScopeTypeMustMatchFinalizedModel))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.ScopelessHierarchyRejectsForScope))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.NonHierarchyEntityIsRejectedImmediately))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.TreeIdTypeMustMatchFinalizedModel))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.NodeKeyTypeMustMatchFinalizedModel))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.MutationRequiresConfiguredScopeBinding))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.MutationTreeIdTypeMustMatchFinalizedModel))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.MutationNodeKeyTypeMustMatchFinalizedModel))]
    [InlineData(typeof(BulkInputTests), nameof(BulkInputTests.BranchCopiesItsChildCollection))]
    [InlineData(typeof(TableMappingTests), nameof(TableMappingTests.EntitySplittingPlacesIndexesOnStructureFragment))]
    [InlineData(typeof(TpcSharedCollationTests), nameof(TpcSharedCollationTests.ScopedConcreteParentKeyBelongsToInheritanceRoot))]
    [InlineData(typeof(TpcTests), nameof(TpcTests.PolymorphicTpcHierarchyIsRejected))]
    [InlineData(typeof(SharedTypeTests), nameof(SharedTypeTests.SharedTypeWithoutEntityNameIsRejected))]
    [InlineData(typeof(BroadScopeTests), nameof(BroadScopeTests.ProviderComparerSeparatesReferenceAliases))]
    [InlineData(typeof(BroadNodeKeyTests), nameof(BroadNodeKeyTests.ProviderComparerSeparatesKeyAliases))]
    [InlineData(typeof(BroadTreeIdTests), nameof(BroadTreeIdTests.ProviderComparerSeparatesTreeAliases))]
    [InlineData(typeof(ConvertedTextCollationTests), nameof(ConvertedTextCollationTests.ImplicitIdentityCaptureMatchesRegistryAndSuppliedModel))]
    public void ConnectionFreeMethodsBypassOnlyTheDatabaseHostCheck(
        Type declaringSuite,
        string methodName
    )
    {
        // Arrange
        var suite = CreateTestMethod("SqlServer", nameof(DiscoveryProbe.Ordinary)).TestClass.Class;
        var method = declaringSuite.GetMethod(methodName)!;

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(suite, method);

        // Assert
        Assert.Null(resolved);
    }
}
