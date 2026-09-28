namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies options-based nested-set registration and its context lifetime behavior.</summary>
public sealed class OptionsExtensionTests
{
    /// <summary>
    ///     Options registration installs exact hierarchy metadata without context-level registration hooks.
    /// </summary>
    [Fact]
    public void RegistrationInstallsInfrastructureAndHierarchyConventions()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<EnabledContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
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

        var indexes = nodeEntity
            .GetIndexes()
            .Select(index => new
            {
                Properties = index
                    .Properties
                    .Select(property => property.Name)
                    .ToArray(),
                Descending = index.IsDescending?.ToArray() ?? new bool[index.Properties.Count],
            })
            .ToArray();

        var checks = nodeEntity
            .GetCheckConstraints()
            .Select(constraint => constraint.Sql)
            .OrderBy(expression => expression, StringComparer.Ordinal)
            .ToArray();

        var parent = nodeEntity
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(Node.ParentId)));

        // Assert
        Assert.StartsWith("DokaNestedSetTrees_", registry.GetTableName(), StringComparison.Ordinal);
        Assert.Equal(["Scope", "TreeId"], registry.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(typeof(int), registry.FindProperty("Scope")!.ClrType);
        Assert.Equal(typeof(Guid), registry.FindProperty("TreeId")!.ClrType);
        Assert.Equal(typeof(long), registry.FindProperty("Revision")!.ClrType);
        Assert.Equal(typeof(byte), registry.FindProperty("Lifecycle")!.ClrType);
        Assert.Equal(
            nodeEntity.FindProperty(nameof(Node.Scope))!.GetColumnType(),
            registry.FindProperty("Scope")!.GetColumnType());
        Assert.Equal(
            nodeEntity.FindProperty(nameof(Node.TreeId))!.GetColumnType(),
            registry.FindProperty("TreeId")!.GetColumnType());
        Assert.Equal(nameof(Node.NodeKey), nodeEntity.FindAnnotation("Doka:NestedSet:NodeKey")?.Value);
        Assert.Equal(nameof(Node.TreeId), nodeEntity.FindAnnotation("Doka:NestedSet:TreeId")?.Value);
        Assert.Equal(nameof(Node.Scope), nodeEntity.FindAnnotation("Doka:NestedSet:Scope")?.Value);
        Assert.False(nodeEntity.FindProperty(nameof(Node.TreeId))!.IsNullable);
        Assert.Equal(ValueGenerated.Never, nodeEntity.FindProperty(nameof(Node.TreeId))!.ValueGenerated);
        Assert.Contains(
            nodeEntity.GetKeys(),
            key => key
                .Properties
                .Select(property => property.Name)
                .SequenceEqual([nameof(Node.Scope), nameof(Node.NodeKey)]));
        Assert.Equal(5, indexes.Length);
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual([nameof(Node.Scope), nameof(Node.TreeId), nameof(Node.Left)]));
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual([nameof(Node.Scope), nameof(Node.TreeId), nameof(Node.Right)]));
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual(
                [nameof(Node.Scope), nameof(Node.TreeId), nameof(Node.ParentId), nameof(Node.Position)]));
        Assert.Contains(indexes, index => index.Properties.SequenceEqual([nameof(Node.Scope), nameof(Node.ParentId)]));

        var order = Assert.Single(
            indexes,
            index => index.Properties.SequenceEqual(
            [
                nameof(Node.Scope),
                nameof(Node.TreeId),
                nameof(Node.ParentId),
                nameof(Node.Name),
                nameof(Node.Kind),
                nameof(Node.NodeKey),
            ]));

        Assert.Equal(
            [
                false,
                false,
                false,
                false,
                true,
                false,
            ],
            order.Descending);
        Assert.Equal([nameof(Node.Scope), nameof(Node.ParentId)], parent.Properties.Select(property => property.Name));
        Assert.Equal(
            [nameof(Node.Scope), nameof(Node.NodeKey)],
            parent.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        Assert.Equal(["\"Depth\" >= 0", "\"Left\" >= 1", "\"Position\" >= 0", "\"Right\" > \"Left\""], checks);
    }

    /// <summary>
    ///     A configured hierarchy without options registration fails before its first hierarchy operation.
    /// </summary>
    [Fact]
    public async Task MissingRegistrationHasAnActionableFailure()
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
        Assert.Contains("optionsBuilder.UseNestedSets()", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Options registration runs final validation after application key configuration completes.</summary>
    [Fact]
    public void RegistrationRejectsNodeKeyWithoutUniqueModelKey()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<InvalidContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        using var context = new InvalidContext(options);

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Repeated registration retains one immutable options extension.</summary>
    [Fact]
    public void RepeatedRegistrationIsIdempotent()
    {
        // Arrange
        var optionsBuilder = new DbContextOptionsBuilder<EnabledContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:");

        // Act
        optionsBuilder.UseNestedSets();
        optionsBuilder.UseNestedSets();

        // Assert
        Assert.Single(
            optionsBuilder.Options.Extensions,
            extension => extension.Info.LogFragment.Contains("NestedSets", StringComparison.Ordinal));
    }

    /// <summary>Different physical hierarchy tables receive independent technical registry tables.</summary>
    [Fact]
    public void RegistryTableNameIncludesPhysicalHierarchyIdentity()
    {
        // Arrange
        // Act
        var first = NestedSetTreeRegistryMetadata.TableName("app", "Folders", "Id", null, "TreeId");
        var second = NestedSetTreeRegistryMetadata.TableName("archive", "Folders", "Id", null, "TreeId");
        var third = NestedSetTreeRegistryMetadata.TableName("app", "ArchivedFolders", "Id", null, "TreeId");
        var fourth = NestedSetTreeRegistryMetadata.TableName("app", "Folders", "Id", "TenantId", "TreeId");
        var fifth = NestedSetTreeRegistryMetadata.TableName("app", "Folders", "FolderId", null, "TreeId");
        var sixth = NestedSetTreeRegistryMetadata.TableName("app", "Folders", "Id", null, "HierarchyId");

        // Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(first, third);
        Assert.NotEqual(first, fourth);
        Assert.NotEqual(first, fifth);
        Assert.NotEqual(first, sixth);
        Assert.Equal(first, NestedSetTreeRegistryMetadata.TableName("app", "Folders", "Id", null, "TreeId"));
    }

    /// <summary>Keeps the options convention out of EF Core's internal migration-history model.</summary>
    [Fact]
    public void RegistrationDoesNotPolluteMigrationHistoryModel()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<EnabledContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        using var context = new EnabledContext(options);

        // Act
        var script = context
            .GetService<Microsoft.EntityFrameworkCore.Migrations.IHistoryRepository>()
            .GetCreateIfNotExistsScript();

        // Assert
        Assert.Contains("__EFMigrationsHistory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DokaNestedSetTrees_", script, StringComparison.Ordinal);
    }

    /// <summary>Pooled contexts reuse the model produced by the stateless options extension.</summary>
    [Fact]
    public async Task RegistrationReusesOneModelAcrossPooledContexts()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<EnabledContext>(
            options => options
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets(),
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
    public void RegistrationInstallsPersistenceHookInEitherExtensionOrder(
        bool nestedSetsFirst
    )
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<EnabledContext>().ConfigureTestWarnings();
        var options = nestedSetsFirst
            ? builder
                .UseNestedSets()
                .UseSqlite("Data Source=:memory:")
                .Options
            : builder
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .Options;

        using var context = new EnabledContext(options);

        // Act
        var service = context.GetService<Microsoft.EntityFrameworkCore.Storage.IDatabase>();

        // Assert
        Assert.IsType<NestedSetRelationalDatabase>(service);
    }

    /// <summary>The standard relational database registration is replaced by exactly one persistence hook.</summary>
    [Fact]
    public void RegistrationReplacesStandardRelationalDatabaseService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<
            Microsoft.EntityFrameworkCore.Storage.IDatabase,
            Microsoft.EntityFrameworkCore.Storage.RelationalDatabase>();

        var extension = (IDbContextOptionsExtension)NestedSetOptionsExtension.Instance;

        // Act
        extension.ApplyServices(services);

        // Assert
        var database = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(Microsoft.EntityFrameworkCore.Storage.IDatabase));

        Assert.Equal(typeof(NestedSetRelationalDatabase), database.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, database.Lifetime);
    }

    /// <summary>A provider-specific database service is rejected instead of being silently replaced.</summary>
    [Fact]
    public void RegistrationRejectsProviderSpecificDatabaseService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<Microsoft.EntityFrameworkCore.Storage.IDatabase>(_ =>
            throw new InvalidOperationException("The provider-specific test service is never resolved."));

        IDbContextOptionsExtension extension = NestedSetOptionsExtension.Instance;

        // Act
        var error = Record.Exception(() => extension.ApplyServices(services));

        // Assert
        Assert.IsType<NotSupportedException>(error);
    }

    /// <summary>Provides a context whose only nested-set bootstrap is the options extension.</summary>
    private sealed class EnabledContext : DbContext
    {
        /// <summary>Creates a context from externally configured options.</summary>
        /// <param name="options">The context options.</param>
        public EnabledContext(
            DbContextOptions<EnabledContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<Node>();
            entity.HasKey(node => new
            {
                node.Scope,
                node.RecordId,
            });
            entity.HasAlternateKey(node => new
            {
                node.Scope,
                node.NodeKey,
            });
            ConfigureNestedSet(entity);
        }
    }

    /// <summary>Provides an invalid model used to prove options-installed final validation.</summary>
    private sealed class InvalidContext : DbContext
    {
        /// <summary>Creates a context from externally configured options.</summary>
        /// <param name="options">The context options.</param>
        public InvalidContext(
            DbContextOptions<InvalidContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<Node>();
            entity.HasKey(node => new
            {
                node.RecordId,
                node.TreeId,
            });
            ConfigureNestedSet(entity);
        }
    }

    /// <summary>Provides a configured hierarchy without nested-set options registration.</summary>
    private sealed class PlainContext : DbContext
    {
        /// <summary>Creates a context from externally configured options.</summary>
        /// <param name="options">The context options.</param>
        public PlainContext(
            DbContextOptions<PlainContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<Node>();
            entity.HasKey(node => node.NodeKey);
            ConfigureNestedSet(entity);
        }
    }

    /// <summary>Supplies all structural properties used by the registered convention.</summary>
    private sealed class Node
    {
        /// <summary>Gets or sets the scope-local record identity.</summary>
        public int RecordId { get; set; }

        /// <summary>Gets or sets the stable scalar hierarchy identity.</summary>
        public Guid NodeKey { get; set; }

        /// <summary>Gets or sets the optional application partition.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the parent node identity.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>Gets or sets the first sibling order value.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the descending sibling order value.</summary>
        public int Kind { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the persisted depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
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
