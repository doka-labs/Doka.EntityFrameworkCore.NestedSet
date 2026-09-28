namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Protects native physical identities on a secondary entity-splitting structure fragment.</summary>
public abstract class EntitySplitCollationTestBase : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Reuses the provider fixture while this model creates distinct payload and structural tables.</summary>
    /// <param name="fixture">The owner of the model-compatibility database.</param>
    protected EntitySplitCollationTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Binary secondary-table Scope identities override the CI model default in public reservations.</summary>
    /// <returns>A task that completes after verifying independent trees, payload, and native registry parity.</returns>
    [Fact]
    public async Task SecondaryFragmentReservesIndependentCaseDistinctScopes()
    {
        // Arrange
        await using var context = await CreateContextAsync();
        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "SPLIT-" + suffix;
        var lowerScope = scope.ToLowerInvariant();
        var treeId = Guid.NewGuid();
        var first = new EntitySplitCollationNode
        {
            Id = "FIRST-" + suffix,
            Name = "First root",
            Payload = "Original first payload",
        };

        await context
            .NestedSet<EntitySplitCollationNode>()
            .ForScope(scope)
            .InsertRootAsync(first, treeId, CancellationToken.None);

        var before = Snapshot(first);
        context.ChangeTracker.Clear();
        var second = new EntitySplitCollationNode
        {
            Id = "SECOND-" + suffix,
            Name = "Second root",
            Payload = "Independent second payload",
        };

        // Act
        await context
            .NestedSet<EntitySplitCollationNode>()
            .ForScope(lowerScope)
            .InsertRootAsync(second, treeId, CancellationToken.None);

        // Assert
        var firstTree = context
            .NestedSet<EntitySplitCollationNode>()
            .ForScope(scope)
            .InTree(treeId);

        var secondTree = context
            .NestedSet<EntitySplitCollationNode>()
            .ForScope(lowerScope)
            .InTree(treeId);

        var persistedFirst = await firstTree.Nodes.SingleAsync(CancellationToken.None);
        var persistedSecond = await secondTree.Nodes.SingleAsync(CancellationToken.None);
        Assert.Equal(before, Snapshot(persistedFirst));
        Assert.Equal(
            (scope, treeId, 1L, 2L, 0, 0L),
            (persistedFirst.Scope, persistedFirst.TreeId, persistedFirst.Left, persistedFirst.Right,
                persistedFirst.Depth, persistedFirst.Position));
        Assert.Equal(
            (second.Id, lowerScope, treeId, null, 1L, 2L, 0, 0L, second.Name, second.Payload),
            Snapshot(persistedSecond));
        Assert.True((await firstTree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True((await secondTree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);

        var entityType = context.Model.FindEntityType(typeof(EntitySplitCollationNode))!;
        var mapping = NestedSetMapping<EntitySplitCollationNode, string, string>.For(context, entityType);
        Assert.Equal(EntitySplitCollationContext.StructuralTable, mapping.Store.Name);
        Assert.Equal(EntitySplitCollationContext.PayloadTable, entityType.GetTableName());
        Assert.NotEqual(entityType.GetTableName(), mapping.Store.Name);
        Assert.Contains(
            entityType.GetMappingFragments(StoreObjectType.Table),
            fragment => fragment.StoreObject == mapping.Store);
        Assert.Equal("utf8mb4_bin", mapping.KeyCollation);

        var designModel = context.GetService<IDesignTimeModel>().Model;
        var designEntity = designModel.FindEntityType(entityType.Name)!;
        var primary = StoreObjectIdentifier.Table(EntitySplitCollationContext.PayloadTable, designEntity.GetSchema());
        Assert.Equal("utf8mb4_unicode_ci", designModel.GetCollation());
        Assert.Equal("utf8mb4_bin", designEntity.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);
        Assert.All(
            new[]
            {
                nameof(EntitySplitCollationNode.Id),
                nameof(EntitySplitCollationNode.Scope),
                nameof(EntitySplitCollationNode.ParentId),
            },
            name =>
            {
                var property = designEntity.FindProperty(name)!;
                Assert.Null(property.GetCollation());
                Assert.Null(property.GetCollation(mapping.Store));
                Assert.NotNull(property.GetColumnName(mapping.Store));
                Assert.Equal(
                    "utf8mb4_bin",
                    NestedSetCollations.Resolve(context, entityType.FindProperty(name)!, entityType));
            });

        Assert.Null(designEntity.FindProperty(nameof(EntitySplitCollationNode.Scope))!.GetColumnName(primary));
        Assert.Null(designEntity.FindProperty(nameof(EntitySplitCollationNode.Left))!.GetColumnName(primary));
        Assert.Null(designEntity.FindProperty(nameof(EntitySplitCollationNode.Payload))!.GetColumnName(mapping.Store));
        var registry = NestedSetTreeRegistryMapping.For(entityType).Registry;
        var designRegistry = designModel.FindEntityType(registry.Name)!;
        Assert.Equal("utf8mb4_bin", designRegistry.FindProperty(NestedSetTreeRegistryMetadata.Scope)!.GetCollation());
        var registries = await RegistrySnapshotsAsync(context, treeId);
        Assert.Equal(
            new[] { scope, lowerScope }.OrderBy(value => value, StringComparer.Ordinal),
            registries.Select(row => row.Scope));
        Assert.All(registries, row => Assert.Equal(NestedSetTreeRegistryMetadata.Active, row.Lifecycle));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Parent lookup uses the secondary fragment's binary key rather than its CI model default.</summary>
    /// <returns>A task that completes after verifying input, persisted values, registry, and tracker state.</returns>
    [Fact]
    public async Task SecondaryFragmentRejectsBinaryParentAliasWithoutWrites()
    {
        // Arrange
        await using var context = await CreateContextAsync();
        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "PARENT-" + suffix;
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<EntitySplitCollationNode>()
            .ForScope(scope);

        var root = new EntitySplitCollationNode
        {
            Id = "ROOT-" + suffix,
            Name = "Root",
            Payload = "Original root payload",
        };

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(
            new EntitySplitCollationNode
            {
                Id = "VALID-" + suffix,
                Name = "Valid child",
                Payload = "Original child payload",
            },
            root.Id,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var before = await TreeSnapshotsAsync(context, scope, treeId);
        var registryBefore = await RegistrySnapshotsAsync(context, treeId);
        var rejected = RejectedInput("REJECTED-" + suffix);
        var inputBefore = Snapshot(rejected);

        // Act
        var failure = await Record.ExceptionAsync(() => hierarchy.InsertChildAsync(
            rejected,
            root.Id.ToLowerInvariant(),
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.NodeNotFound, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(inputBefore, Snapshot(rejected));
        Assert.Equal(EntityState.Detached, context.Entry(rejected).State);
        Assert.Equal(before, await TreeSnapshotsAsync(context, scope, treeId));
        Assert.Equal(registryBefore, await RegistrySnapshotsAsync(context, treeId));
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>An exact duplicate reservation preserves both fragments and all caller-owned input values.</summary>
    /// <returns>A task that completes after verifying rollback of the failed public root insertion.</returns>
    [Fact]
    public async Task SecondaryFragmentRejectsDuplicateReservationWithoutWrites()
    {
        // Arrange
        await using var context = await CreateContextAsync();
        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "DUPLICATE-" + suffix;
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<EntitySplitCollationNode>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(
            new EntitySplitCollationNode
            {
                Id = "ROOT-" + suffix,
                Name = "Root",
                Payload = "Preserved root payload",
            },
            treeId,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var before = await TreeSnapshotsAsync(context, scope, treeId);
        var registryBefore = await RegistrySnapshotsAsync(context, treeId);
        var rejected = RejectedInput("REJECTED-" + suffix);
        var inputBefore = Snapshot(rejected);

        // Act
        var failure = await Record.ExceptionAsync(() => hierarchy.InsertRootAsync(
            rejected,
            treeId,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(inputBefore, Snapshot(rejected));
        Assert.Equal(EntityState.Detached, context.Entry(rejected).State);
        Assert.Equal(before, await TreeSnapshotsAsync(context, scope, treeId));
        Assert.Equal(registryBefore, await RegistrySnapshotsAsync(context, treeId));
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Creates this distinct split model on the existing provider database.</summary>
    private Task<EntitySplitCollationContext> CreateContextAsync() =>
        _fixture.CreateContextAsync<EntitySplitCollationContext>(
            Engine,
            static options => new EntitySplitCollationContext(options));

    /// <summary>Creates distinctive input values so exact rollback cannot be mistaken for a default reset.</summary>
    private static EntitySplitCollationNode RejectedInput(
        string id
    ) => new()
    {
        Id = id,
        Scope = "caller-scope",
        TreeId = Guid.NewGuid(),
        ParentId = "caller-parent",
        Left = 71,
        Right = 72,
        Depth = 17,
        Position = 23,
        Name = "Rejected",
        Payload = "Unwritten payload",
    };

    /// <summary>Captures every field joined from the payload and structural fragments.</summary>
    private static (string Id, string Scope, Guid TreeId, string? Parent, long Left, long Right, int Depth, long
        Position, string Name, string Payload) Snapshot(
            EntitySplitCollationNode node
        ) => (node.Id, node.Scope, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth, node.Position,
        node.Name,
        node.Payload);

    /// <summary>Reads complete persisted entity values without introducing tracked state.</summary>
    private static async Task<(string Id, string Scope, Guid TreeId, string? Parent, long Left, long Right, int Depth,
        long Position, string Name, string Payload)[]> TreeSnapshotsAsync(
        EntitySplitCollationContext context,
        string scope,
        Guid treeId
    )
    {
        var nodes = await context
            .NestedSet<EntitySplitCollationNode>()
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        return nodes
            .Select(Snapshot)
            .ToArray();
    }

    /// <summary>Captures registry identity, revision, and lifecycle for exact failed-mutation comparison.</summary>
    private static Task<(string Scope, Guid TreeId, long Revision, byte Lifecycle)[]> RegistrySnapshotsAsync(
        EntitySplitCollationContext context,
        Guid treeId
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(EntitySplitCollationNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy).Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Where(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == treeId)
            .OrderBy(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope))
            .Select(row => new ValueTuple<string, Guid, long, byte>(
                EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope),
                EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(CancellationToken.None);
    }
}
