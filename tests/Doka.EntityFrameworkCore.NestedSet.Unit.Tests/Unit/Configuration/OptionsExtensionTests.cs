using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies options-based nested-set registration and its context lifetime behavior.</summary>
public sealed partial class OptionsExtensionTests
{
    /// <summary>
    ///     Options registration installs exact hierarchy metadata without context-level registration hooks.
    /// </summary>
    [Fact]
    public void GetEntityTypes_Registration_InstallsInfrastructureAndHierarchyConventions()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<EnabledContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets())
            .Options;

        using var context = new EnabledContext(options);

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var nodeEntity = model.FindEntityType(typeof(Node))
            ?? throw new InvalidOperationException("The enabled test model did not contain its hierarchy entity.");

        var registry = model
            .GetEntityTypes()
            .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

        // Assert
        AssertRegistryMetadata(nodeEntity, registry);
        AssertHierarchyRoles(nodeEntity);
        AssertHierarchyIndexes(nodeEntity);
        AssertParentRelationship(nodeEntity);
        AssertHierarchyChecks(nodeEntity);
    }

    /// <summary>
    ///     A configured hierarchy without options registration fails before its first hierarchy operation.
    /// </summary>
    [Fact]
    public async Task InsertRootAsync_MissingRegistration_HasAnActionableFailure()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<PlainContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var context = new PlainContext(options);

        // Act
        var exception = await Record.ExceptionAsync(() => context
            .NestedSet<Node>()
            .ForScope(7)
            .InsertRootAsync(new Node { NodeKey = Guid.NewGuid() }, Guid.NewGuid(), CancellationToken.None));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("UseNestedSets()", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Options registration runs final validation after application key configuration completes.</summary>
    [Fact]
    public void FinalizeModel_Registration_RejectsNodeKeyWithoutUniqueModelKey()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<InvalidContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets())
            .Options;

        using var context = new InvalidContext(options);

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Repeated registration retains one immutable options extension.</summary>
    [Fact]
    public void UseNestedSets_RepeatedRegistration_IsIdempotent()
    {
        // Arrange
        var optionsBuilder = new DbContextOptionsBuilder<EnabledContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:");

        var native = new SqliteDbContextOptionsBuilder(optionsBuilder);

        // Act
        var firstBuilder = native.UseNestedSets();
        var first = optionsBuilder.Options.FindExtension<NestedSetOptionsExtension>();
        var secondBuilder = native.UseNestedSets();
        var second = optionsBuilder.Options.FindExtension<NestedSetOptionsExtension>();

        // Assert
        Assert.Same(native, firstBuilder);
        Assert.Same(native, secondBuilder);
        Assert.Same(first, second);
        Assert.Same(NestedSetTestAdapters.ForEngine("Sqlite"), second!.Provider);
        Assert.Single(optionsBuilder.Options.Extensions.OfType<NestedSetOptionsExtension>());
    }

    /// <summary>Different physical hierarchy tables receive independent technical registry tables.</summary>
    [Fact]
    public void TableName_RegistryTableName_IncludesPhysicalHierarchyIdentity()
    {
        // Arrange
        const string schema = "app";
        const string table = "Folders";
        const string keyColumn = "Id";
        const string treeColumn = "TreeId";

        // Act
        var first = NestedSetTreeRegistryMetadata.TableName(schema, table, keyColumn, null, treeColumn);
        var second = NestedSetTreeRegistryMetadata.TableName("archive", table, keyColumn, null, treeColumn);
        var third = NestedSetTreeRegistryMetadata.TableName(schema, "ArchivedFolders", keyColumn, null, treeColumn);
        var fourth = NestedSetTreeRegistryMetadata.TableName(schema, table, keyColumn, "TenantId", treeColumn);
        var fifth = NestedSetTreeRegistryMetadata.TableName(schema, table, "FolderId", null, treeColumn);
        var sixth = NestedSetTreeRegistryMetadata.TableName(schema, table, keyColumn, null, "HierarchyId");
        var repeated = NestedSetTreeRegistryMetadata.TableName(schema, table, keyColumn, null, treeColumn);

        // Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(first, third);
        Assert.NotEqual(first, fourth);
        Assert.NotEqual(first, fifth);
        Assert.NotEqual(first, sixth);
        Assert.Equal(first, repeated);
    }

    /// <summary>Keeps the options convention out of EF Core's internal migration-history model.</summary>
    [Fact]
    public void GetCreateIfNotExistsScript_Registration_DoesNotPolluteMigrationHistoryModel()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<EnabledContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets())
            .Options;

        using var context = new EnabledContext(options);

        // Act
        var script = context
            .GetService<IHistoryRepository>()
            .GetCreateIfNotExistsScript();

        // Assert
        Assert.Contains("__EFMigrationsHistory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DokaNestedSetTrees_", script, StringComparison.Ordinal);
    }

    /// <summary>Pooled contexts reuse the model produced by the stateless options extension.</summary>
    [Fact]
    public async Task CreateDbContextAsync_Registration_ReusesOneModelAcrossPooledContexts()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<EnabledContext>(
            options => options
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets()),
            1);

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<EnabledContext>>();

        // Act
        IModel firstModel;
        await using (var first = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            firstModel = first.Model;
        }

        await using var second = await factory.CreateDbContextAsync(CancellationToken.None);

        // Assert
        Assert.Same(firstModel, second.Model);
        Assert.Contains(second.Model.GetEntityTypes(), entity => entity.ClrType == typeof(NestedSetTreeRegistry));
    }

    /// <summary>The persistence boundary is installed whether the provider is configured before or after it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetService_Registration_InstallsPersistenceHookInEitherExtensionOrder(
        bool nestedSetsFirst
    )
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<EnabledContext>().ConfigureTestWarnings();

        if (nestedSetsFirst)
        {
            // WHY: Some native builders invoke their callback before their own options update completes.
            // Registration must therefore defer actual-provider validation until context initialization.
            NestedSetProviderRegistration.Register(builder, NestedSetTestAdapters.ForEngine("Sqlite"));
        }

        var options = builder
            .UseSqlite(
                "Data Source=:memory:",
                sqlite =>
                {
                    if (!nestedSetsFirst)
                    {
                        sqlite.UseNestedSets();
                    }
                })
            .Options;

        using var context = new EnabledContext(options);

        // Act
        var service = context.GetService<IDatabase>();

        // Assert
        Assert.IsType<NestedSetRelationalDatabase>(service);
    }

    /// <summary>The standard relational database registration is replaced by exactly one persistence hook.</summary>
    [Fact]
    public void ApplyServices_Registration_ReplacesStandardRelationalDatabaseService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<
            IDatabase,
            RelationalDatabase>();

        var extension =
            (IDbContextOptionsExtension)new NestedSetOptionsExtension(NestedSetTestAdapters.ForEngine("Sqlite"));

        // Act
        extension.ApplyServices(services);

        // Assert
        var database = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IDatabase));

        Assert.Equal(typeof(NestedSetRelationalDatabase), database.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, database.Lifetime);
    }

    /// <summary>A provider-specific database service is rejected instead of being silently replaced.</summary>
    [Fact]
    public void ApplyServices_Registration_RejectsProviderSpecificDatabaseService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IDatabase>(_ =>
            throw new InvalidOperationException("The provider-specific test service is never resolved."));

        IDbContextOptionsExtension extension = new NestedSetOptionsExtension(NestedSetTestAdapters.ForEngine("Sqlite"));

        // Act
        var error = Record.Exception(() => extension.ApplyServices(services));

        // Assert
        Assert.IsType<NotSupportedException>(error);
    }

    /// <summary>Configures the complete structural mapping shared by positive and negative model tests.</summary>
    /// <param name="entity">The node entity builder.</param>
    private static void ConfigureNestedSet(
        EntityTypeBuilder<Node> entity
    ) => entity.HasNestedSet(nestedSet => nestedSet
        .HasNodeKey(node => node.NodeKey)
        .HasScope(node => node.Scope)
        .HasTreeId(node => node.TreeId)
        .HasParent(node => node.ParentId)
        .HasBounds(node => node.Left, node => node.Right)
        .HasDepth(node => node.Depth)
        .HasPosition(node => node.Position)
        .OrderBy(node => node.Name)
        .ThenByDescending(node => node.Kind));
}
