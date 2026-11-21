namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class EngineTestDiscoveryTests
{
    /// <summary>
    /// Database applicability reads the concrete engine and requires an explicit metadata-only marker.
    /// </summary>
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
    public void ResolveDatabaseEngine_PlatformHook_UsesConcreteEngineWithoutActivatingResources(
        string engine,
        bool databaseIndependent
    )
    {
        // Arrange
        var method = CreateTestMethod(
            engine,
            nameof(DiscoveryProbe.Ordinary),
            databaseIndependent: databaseIndependent);

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(method.TestClass.Class, method.Method);

        // Assert
        Assert.Equal(
            databaseIndependent
                ? null
                : engine,
            resolved);
    }

    /// <summary>An identity-only fixture is not evidence that a test body avoids the database.</summary>
    [Fact]
    public void ResolveDatabaseEngine_ProviderResources_DoesNotBypassSqlServerPlatformCheck()
    {
        // Arrange
        var method = CreateTestMethod(
            "SqlServer",
            nameof(DiscoveryProbe.Ordinary),
            [typeof(ProviderFixture<ProviderResources, SqlServerEngine>)]);

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(method.TestClass.Class, method.Method);

        // Assert
        Assert.Equal("SqlServer", resolved);
    }

    /// <summary>
    /// The shared root registers xUnit's normal execution hook and metadata guards explicitly opt out.
    /// </summary>
    [Fact]
    public void GetCustomAttribute_SharedRoot_OwnsPlatformHookAndMetadataMarker()
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

    /// <summary>
    /// Database-free lifecycle methods run while the neighboring database isolation case stays guarded.
    /// </summary>
    [Theory]
    [InlineData(nameof(DatabaseLifecycleTests.GetCurrentAsync_SharedLibrary_ResolvesExecutingAssemblyFixture), true)]
    [InlineData(nameof(DatabaseLifecycleTests.NewConnectionStringAsync_AssemblyFixture_RejectsForeignServer), true)]
    [InlineData(nameof(DatabaseLifecycleTests.SaveChangesAsync_ServerDatabases_ShareEngineWithoutSharingRows), false)]
    public void ResolveDatabaseEngine_DatabaseIndependentMethod_DoesNotDisableItsWholeSuite(
        string methodName,
        bool databaseIndependent
    )
    {
        // Arrange
        var suite = CreateTestMethod(
                "SqlServer",
                nameof(DiscoveryProbe.Ordinary),
                [typeof(ProviderFixture<ProviderResources, SqlServerEngine>)])
            .TestClass.Class;

        var method = typeof(DatabaseLifecycleTests).GetMethod(methodName)!;

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(suite, method);

        // Assert
        Assert.Equal(
            databaseIndependent
                ? null
                : "SqlServer",
            resolved);
    }

    /// <summary>SQL Server compiled-model metadata remains available without starting a server.</summary>
    [Theory]
    [InlineData(nameof(CompiledModelTests.GetEntityTypes_DesignModel_UsesTypedTreeRegistries))]
    [InlineData(nameof(CompiledModelTests.GetEntityTypes_CompiledRegistryNames_MatchDesignModel))]
    [InlineData(nameof(CompiledModelTests.FindAnnotation_CompiledStringIdentity_CapturesMatchDesignModel))]
    public void ResolveDatabaseEngine_CompiledModelMetadata_DoesNotRequireSqlServer(
        string methodName
    )
    {
        // Arrange
        var suite = CreateTestMethod("SqlServer", nameof(DiscoveryProbe.Ordinary))
            .TestClass.Class;

        var method = typeof(CompiledModelTests).GetMethod(methodName)!;

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(suite, method);

        // Assert
        Assert.Null(resolved);
    }

    /// <summary>Connection-free binding, model, and value-comparer cases stay runnable on every local host.</summary>
    [Theory]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.InTree_ScopedHierarchy_RequiresForScope))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.ForScope_ScopeType_MustMatchFinalizedModel))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.ForScope_ScopelessHierarchy_RejectsForScope))]
    [InlineData(
        typeof(NestedSetFacadeTests),
        nameof(NestedSetFacadeTests.NestedSet_NonHierarchyEntity_IsRejectedImmediately))]
    [InlineData(typeof(NestedSetFacadeTests), nameof(NestedSetFacadeTests.InTree_TreeIdType_MustMatchFinalizedModel))]
    [InlineData(
        typeof(NestedSetFacadeTests),
        nameof(NestedSetFacadeTests.AncestorsOf_NodeKeyType_MustMatchFinalizedModel))]
    [InlineData(
        typeof(NestedSetFacadeTests),
        nameof(NestedSetFacadeTests.InsertRootAsync_Mutation_RequiresConfiguredScopeBinding))]
    [InlineData(
        typeof(NestedSetFacadeTests),
        nameof(NestedSetFacadeTests.InsertRootAsync_MutationTreeIdType_MustMatchFinalizedModel))]
    [InlineData(
        typeof(NestedSetFacadeTests),
        nameof(NestedSetFacadeTests.DeleteAsync_MutationNodeKeyType_MustMatchFinalizedModel))]
    [InlineData(typeof(BulkInputTests), nameof(BulkInputTests.Construct_Branch_CopiesItsChildCollection))]
    [InlineData(
        typeof(TableMappingTests),
        nameof(TableMappingTests.GenerateCreateScript_EntitySplitting_PlacesIndexesOnStructureFragment))]
    [InlineData(
        typeof(TpcSharedCollationTests),
        nameof(TpcSharedCollationTests.FinalizeModel_ScopedConcreteParentKey_BelongsToInheritanceRoot))]
    [InlineData(typeof(TpcTests), nameof(TpcTests.NestedSet_PolymorphicTpcHierarchy_IsRejected))]
    [InlineData(typeof(SharedTypeTests), nameof(SharedTypeTests.NestedSet_SharedTypeWithoutEntityName_IsRejected))]
    [InlineData(typeof(BroadScopeTests), nameof(BroadScopeTests.Equals_ProviderComparer_SeparatesReferenceAliases))]
    [InlineData(typeof(BroadNodeKeyTests), nameof(BroadNodeKeyTests.Matches_ProviderComparer_SeparatesKeyAliases))]
    [InlineData(typeof(BroadTreeIdTests), nameof(BroadTreeIdTests.Equals_ProviderComparer_SeparatesTreeAliases))]
    [InlineData(
        typeof(ConvertedTextCollationTests),
        nameof(ConvertedTextCollationTests.UseModel_ImplicitIdentityCapture_MatchesRegistryAndSuppliedModel))]
    public void ResolveDatabaseEngine_ConnectionFreeMethods_BypassOnlyTheDatabaseHostCheck(
        Type declaringSuite,
        string methodName
    )
    {
        // Arrange
        var suite = CreateTestMethod("SqlServer", nameof(DiscoveryProbe.Ordinary))
            .TestClass.Class;

        var method = declaringSuite.GetMethod(methodName)!;

        // Act
        var resolved = ProviderDatabasePlatformAttribute.ResolveDatabaseEngine(suite, method);

        // Assert
        Assert.Null(resolved);
    }
}
