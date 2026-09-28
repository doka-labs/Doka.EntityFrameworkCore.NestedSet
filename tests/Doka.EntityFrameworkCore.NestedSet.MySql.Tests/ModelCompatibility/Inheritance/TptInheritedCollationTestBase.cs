namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Protects Doka's original TPT mapping-owner collation when identities live in an ancestor table.</summary>
public abstract class TptInheritedCollationTestBase : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the existing isolated provider database for the distinct structural and payload tables.</summary>
    /// <param name="fixture">The owner of the model-compatibility database.</param>
    protected TptInheritedCollationTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Two incompatible owner defaults for one physical identity column reject during model capture.</summary>
    [Fact]
    public void ConflictingAncestorOwnerCollationsRejectModelCapture()
    {
        // Arrange
        using var context = new ConflictingTptCollationContext(
            ModelCompatibilityDatabase.Options<ConflictingTptCollationContext>(Engine));

        // Act
        var failure = Record.Exception(() => _ = context.Model);

        // Assert
        var error = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("conflicting canonical table collations", error.Message, StringComparison.Ordinal);
        Assert.Contains(TptInheritedCollationContext.StructuralTable, error.Message, StringComparison.Ordinal);
        Assert.Contains("utf8mb4_bin", error.Message, StringComparison.Ordinal);
        Assert.Contains("utf8mb4_unicode_ci", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A leaf's canonical table facet preserves independent ancestor Scopes sharing the same TreeId.</summary>
    /// <returns>A task that completes after verifying rows, effective facets, and registry isolation.</returns>
    [Fact]
    public async Task LeafTableCollationPreservesIndependentAncestorScopes()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptInheritedCollationContext>(
            Engine,
            static options => new TptInheritedCollationContext(options));

        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var firstScope = "TPT-" + suffix;
        var secondScope = firstScope.ToLowerInvariant();
        var treeId = Guid.NewGuid();
        var first = new TptInheritedCollationLeaf
        {
            Id = "FIRST-" + suffix,
            Name = "First root",
            Payload = "First leaf payload",
        };

        await context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(firstScope)
            .InsertRootAsync(first, treeId, CancellationToken.None);

        var before = Snapshot(first);
        context.ChangeTracker.Clear();
        var second = new TptInheritedCollationLeaf
        {
            Id = "SECOND-" + suffix,
            Name = "Second root",
            Payload = "Second leaf payload",
        };

        // Act
        await context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(secondScope)
            .InsertRootAsync(second, treeId, CancellationToken.None);

        // Assert
        var firstTree = context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(firstScope)
            .InTree(treeId);

        var secondTree = context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(secondScope)
            .InTree(treeId);

        var persistedFirst = Assert.IsType<TptInheritedCollationLeaf>(
            await firstTree.Nodes.SingleAsync(CancellationToken.None));

        var persistedSecond = Assert.IsType<TptInheritedCollationLeaf>(
            await secondTree.Nodes.SingleAsync(CancellationToken.None));

        Assert.Equal(before, Snapshot(persistedFirst));
        Assert.Equal(
            (firstScope, treeId, 1L, 2L, 0, 0L),
            (persistedFirst.Scope, persistedFirst.TreeId, persistedFirst.Left, persistedFirst.Right,
                persistedFirst.Depth, persistedFirst.Position));
        Assert.Equal(
            (second.Id, secondScope, treeId, null, 1L, 2L, 0, 0L, second.Name, second.Payload),
            Snapshot(persistedSecond));
        Assert.True((await firstTree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True((await secondTree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);

        var entityType = context.Model.FindEntityType(typeof(TptInheritedCollationNode))!;
        var leafType = context.Model.FindEntityType(typeof(TptInheritedCollationLeaf))!;
        var mapping = NestedSetMapping<TptInheritedCollationNode, string, string>.For(context, entityType);
        Assert.Equal(TptInheritedCollationContext.StructuralTable, mapping.Store.Name);
        Assert.Equal(TptInheritedCollationContext.PayloadTable, leafType.GetTableName());
        Assert.NotEqual(mapping.Store.Name, leafType.GetTableName());
        Assert.Equal("utf8mb4_bin", mapping.KeyCollation);
        Assert.All(
            new[]
            {
                nameof(TptInheritedCollationNode.Id),
                nameof(TptInheritedCollationNode.Scope),
                nameof(TptInheritedCollationNode.ParentId),
            },
            name => Assert.Equal(
                "utf8mb4_bin",
                NestedSetCollations.Resolve(context, entityType.FindProperty(name)!, entityType)));

        var designModel = context.GetService<IDesignTimeModel>()
            .Model;
        var designBase = designModel.FindEntityType(entityType.Name)!;
        var designLeaf = designModel.FindEntityType(leafType.Name)!;
        Assert.Null(designModel.GetCollation());
        Assert.Null(
            designBase.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);
        Assert.Equal(
            "utf8mb4_bin",
            designLeaf.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);
        Assert.All(
            new[]
            {
                nameof(TptInheritedCollationNode.Id),
                nameof(TptInheritedCollationNode.Scope),
                nameof(TptInheritedCollationNode.ParentId),
            },
            name => Assert.Null(designBase.FindProperty(name)!.GetCollation()));

        var registry = NestedSetTreeRegistryMapping.For(entityType).Registry;
        var designRegistry = designModel.FindEntityType(registry.Name)!;
        Assert.Equal("utf8mb4_bin", designRegistry.FindProperty(NestedSetTreeRegistryMetadata.Scope)!.GetCollation());
        var scopes = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Where(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == treeId)
            .OrderBy(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope))
            .Select(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope))
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(new[] { firstScope, secondScope }.OrderBy(scope => scope, StringComparer.Ordinal), scopes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A concrete facade resolves inherited string identities through the configured base capture.</summary>
    /// <returns>A task that completes after verifying the parent, child, and inherited capture.</returns>
    [Fact]
    public async Task ConcreteLeafFacadeUsesConfiguredAncestorCaptures()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptInheritedCollationContext>(
            Engine,
            static options => new TptInheritedCollationContext(options));

        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "FACADE-" + suffix;
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(scope);

        var root = new TptInheritedCollationLeaf
        {
            Id = "ROOT-" + suffix,
            Name = "Root",
            Payload = "Root leaf payload",
        };

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        var child = new TptInheritedCollationLeaf
        {
            Id = "CHILD-" + suffix,
            Name = "Child",
            Payload = "Child leaf payload",
        };

        await hierarchy.InsertChildAsync(child, root.Id, CancellationToken.None);
        context.ChangeTracker.Clear();
        var before = await TreeSnapshotsAsync(context, scope, treeId);

        // Act
        var parent = await context
            .NestedSet<TptInheritedCollationLeaf>()
            .ForScope(scope)
            .ParentOf(child.Id)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(before[0], Snapshot(parent));
        Assert.Equal(root.Id, parent.Id);
        Assert.Equal((1L, 4L, 0, 0L), (parent.Left, parent.Right, parent.Depth, parent.Position));
        Assert.Equal(before, await TreeSnapshotsAsync(context, scope, treeId));
        var leafType = context.Model.FindEntityType(typeof(TptInheritedCollationLeaf))!;
        Assert.Equal(
            "utf8mb4_bin",
            NestedSetCollations.Resolve(
                context,
                leafType.FindProperty(nameof(TptInheritedCollationNode.Scope))!,
                leafType));
        Assert.Equal(
            "utf8mb4_bin",
            NestedSetMapping<TptInheritedCollationLeaf, string, string>.For(context, leafType)
                .KeyCollation);
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The concrete facade cannot substitute default CI equality for its binary ancestor key.</summary>
    /// <returns>A task that completes after verifying exact input and persisted-state preservation.</returns>
    [Fact]
    public async Task ConcreteLeafFacadeRejectsBinaryParentKeyAlias()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptInheritedCollationContext>(
            Engine,
            static options => new TptInheritedCollationContext(options));

        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "BINARY-KEY-" + suffix;
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(scope);

        var root = new TptInheritedCollationLeaf
        {
            Id = "ROOT-" + suffix,
            Name = "Root",
            Payload = "Original root payload",
        };

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        context.ChangeTracker.Clear();
        var before = await TreeSnapshotsAsync(context, scope, treeId);
        var child = new TptInheritedCollationLeaf
        {
            Id = "REJECTED-" + suffix,
            Name = "Child",
            Payload = "Unwritten leaf payload",
        };

        var inputBefore = Snapshot(child);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<TptInheritedCollationLeaf>()
            .ForScope(scope)
            .InsertChildAsync(child, root.Id.ToLowerInvariant(), CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.NodeNotFound,
            Assert.IsType<NestedSetException>(failure)
                .Code);
        Assert.Equal(inputBefore, Snapshot(child));
        Assert.Equal(
            EntityState.Detached,
            context.Entry(child)
                .State);
        Assert.Equal(before, await TreeSnapshotsAsync(context, scope, treeId));
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Normal concrete leaf saves coordinate a Parent change using the configured ancestor capture.</summary>
    /// <returns>A task that completes after verifying geometry, payload, and accepted tracker state.</returns>
    [Fact]
    public async Task ConcreteLeafParentSaveUsesConfiguredAncestorCaptures()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptInheritedCollationContext>(
            Engine,
            static options => new TptInheritedCollationContext(options));

        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "PARENT-SAVE-" + suffix;
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(scope);

        var root = new TptInheritedCollationLeaf
        {
            Id = "ROOT-" + suffix,
            Name = "Root",
            Payload = "Root leaf payload",
        };

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        var moved = new TptInheritedCollationLeaf
        {
            Id = "MOVED-" + suffix,
            Name = "Moved",
            Payload = "Original moved payload",
        };

        await hierarchy.InsertChildAsync(moved, root.Id, CancellationToken.None);
        var target = new TptInheritedCollationLeaf
        {
            Id = "TARGET-" + suffix,
            Name = "Target",
            Payload = "Target leaf payload",
        };

        await hierarchy.InsertChildAsync(target, root.Id, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<TptInheritedCollationLeaf>()
            .SingleAsync(node => node.Id == moved.Id, CancellationToken.None);

        changed.ParentId = target.Id;
        changed.Name = "Reparented";
        changed.Payload = "Reparented leaf payload";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var snapshots = await TreeSnapshotsAsync(context, scope, treeId);
        Assert.Equal(
            new[]
            {
                (root.Id, scope, treeId, null, 1L, 6L, 0, 0L, root.Name, root.Payload),
                (target.Id, scope, treeId, (string?)root.Id, 2L, 5L, 1, 0L, target.Name, target.Payload),
                (moved.Id, scope, treeId, (string?)target.Id, 3L, 4L, 2, 0L, "Reparented",
                    "Reparented leaf payload"),
            },
            snapshots);
        Assert.Equal(snapshots[2], Snapshot(changed));
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(changed)
                .State);
        Assert.Same(
            changed,
            Assert.Single(context.ChangeTracker.Entries())
                .Entity);
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Reads complete ancestor and leaf state in hierarchy order without changing the tracker.</summary>
    private static async Task<(string Id, string Scope, Guid TreeId, string? Parent, long Left, long Right, int Depth,
        long Position, string Name, string Payload)[]> TreeSnapshotsAsync(
        TptInheritedCollationContext context,
        string scope,
        Guid treeId
    )
    {
        var nodes = await context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        return nodes
            .Select(node => Snapshot((TptInheritedCollationLeaf)node))
            .ToArray();
    }

    /// <summary>Captures every ancestor field and the separately stored leaf payload by value.</summary>
    private static (string Id, string Scope, Guid TreeId, string? Parent, long Left, long Right, int Depth, long
        Position, string Name, string Payload) Snapshot(
            TptInheritedCollationLeaf node
        ) => (node.Id, node.Scope, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth, node.Position,
        node.Name,
        node.Payload);
}
