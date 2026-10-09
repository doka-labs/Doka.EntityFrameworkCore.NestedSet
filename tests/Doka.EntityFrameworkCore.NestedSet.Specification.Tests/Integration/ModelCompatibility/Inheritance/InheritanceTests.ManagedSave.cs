namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class InheritanceTests
{
    /// <summary>Moves a concrete metric under a folder through their complete base hierarchy.</summary>
    /// <param name="tpt">Whether structural state uses a TPT base fragment.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedSubtypeParentChangeQueriesConfiguredOwner(
        bool tpt
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(tpt);
        var rootId = tpt ? 4_001 : 5_001;
        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(tpt, false, rootId, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 1, "Folder"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 2, "Metric"), rootId, CancellationToken.None);
        var tracked = await context
            .Set<InheritanceNode>()
            .Where(node => node.TreeId == treeId)
            .ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Id == rootId + 2).ParentId = rootId + 1;

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        var persisted = await PersistedAsync(context, treeId);
        Assert.Equal(new[] { rootId, rootId + 1, rootId + 2 }, persisted.Select(node => node.Id));
        Assert.Equal(rootId + 1, persisted[2].ParentId);
        Assert.Equal(2, persisted[2].Depth);
        Assert.Equal((1L, 6L), (persisted[0].Left, persisted[0].Right));
        AssertTracked(context, tracked, persisted);
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Plans mixed derived moves together and refreshes descendants after crossing trees.</summary>
    /// <param name="tpt">Whether structural state uses a TPT base fragment.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedSubtypeCrossTreeMovesShareDependenciesAndRefreshTracker(
        bool tpt
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(tpt);
        var rootId = tpt ? 6_001 : 7_001;
        var hierarchy = context.NestedSet<InheritanceNode>();
        var sourceTree = Guid.NewGuid();
        var targetTree = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(tpt, false, rootId, "Source"), sourceTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 1, "Branch"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 2, "Descendant"), rootId + 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 3, "Sibling"), rootId, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(tpt, true, rootId + 4, "Target"), targetTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 5, "Leaf"), rootId + 4, CancellationToken.None);
        var tracked = await context
            .Set<InheritanceNode>()
            .Where(node => node.TreeId == sourceTree || node.TreeId == targetTree)
            .ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Id == rootId + 1).ParentId = rootId + 4;
        tracked.Single(node => node.Id == rootId + 5).ParentId = rootId + 2;

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        var source = await PersistedAsync(context, sourceTree);
        var target = await PersistedAsync(context, targetTree);
        Assert.Equal(new[] { rootId, rootId + 3 }, source.Select(node => node.Id));
        Assert.Equal(new[] { rootId + 4, rootId + 1, rootId + 2, rootId + 5 }, target.Select(node => node.Id));
        Assert.Equal(Enumerable.Range(0, 4), target.Select(node => node.Depth));
        Assert.Equal(rootId + 2, target[3].ParentId);
        AssertTracked(
            context,
            tracked,
            source
                .Concat(target)
                .ToArray());
        Assert.True(
            (await hierarchy
                .InTree(sourceTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await hierarchy
                .InTree(targetTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Applies a base ordering rule to mixed derived payload changes and unchanged subtrees.</summary>
    /// <param name="tpt">Whether structural state uses a TPT base fragment.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether saved payload originals should be accepted.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BaseOrderingRefreshesMixedSubtypeSubtrees(
        bool tpt,
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(tpt, ordered: true);
        var rootId = (tpt ? 8_001 : 9_001) + (acceptAllChangesOnSuccess ? 100 : 0);
        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(tpt, false, rootId, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 1, "Bravo"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 2, "Charlie"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 3, "Delta"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 4, "Descendant"), rootId + 3, CancellationToken.None);
        var tracked = await context
            .Set<InheritanceNode>()
            .Where(node => node.TreeId == treeId)
            .ToArrayAsync(CancellationToken.None);

        var folder = tracked.Single(node => node.Id == rootId + 1);
        var metric = tracked.Single(node => node.Id == rootId + 2);
        folder.Name = "Zulu";
        metric.Name = "Alpha";

        // Act
        await context.SaveNestedSetChangesAsync(
            acceptAllChangesOnSuccess,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        var persisted = await PersistedAsync(context, treeId);
        Assert.Equal(
            new[]
            {
                rootId,
                rootId + 2,
                rootId + 3,
                rootId + 4,
                rootId + 1,
            },
            persisted.Select(node => node.Id));

        AssertTracked(context, tracked, persisted);
        var state = acceptAllChangesOnSuccess ? EntityState.Unchanged : EntityState.Modified;
        Assert.Equal(state, context.Entry(folder).State);
        Assert.Equal(state, context.Entry(metric).State);
        Assert.Equal(
            acceptAllChangesOnSuccess ? "Zulu" : "Bravo",
            context
                .Entry(folder)
                .Property(node => node.Name)
                .OriginalValue);
        Assert.Equal(
            acceptAllChangesOnSuccess ? "Alpha" : "Charlie",
            context
                .Entry(metric)
                .Property(node => node.Name)
                .OriginalValue);
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Rejects missing parents and mixed-subtype cycles without losing pending caller state.</summary>
    /// <param name="tpt">Whether structural state uses a TPT base fragment.</param>
    /// <param name="missing">Whether the requested parent is absent instead of a descendant.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvalidMixedSubtypeParentRestoresPendingState(
        bool tpt,
        bool missing
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(tpt);
        var rootId = (tpt ? 10_001 : 11_001) + (missing ? 100 : 0);
        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(tpt, false, rootId, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 1, "Branch"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 2, "Descendant"), rootId + 1, CancellationToken.None);
        var tracked = await context
            .Set<InheritanceNode>()
            .Where(node => node.TreeId == treeId)
            .ToArrayAsync(CancellationToken.None);

        var before = await PersistedAsync(context, treeId);
        var changed = tracked.Single(node => node.Id == rootId + 1);
        var requestedParent = missing ? rootId + 99 : rootId + 2;
        changed.ParentId = requestedParent;
        changed.Name = "Pending";

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None));

        // Assert
        Assert.Equal(
            missing ? NestedSetErrorCode.NodeNotFound : NestedSetErrorCode.CycleDetected,
            Assert.IsType<NestedSetException>(error)
                .Code);

        var persisted = await PersistedAsync(context, treeId);
        Assert.Equal(before.Select(Snapshot), persisted.Select(Snapshot));
        Assert.Equal(requestedParent, changed.ParentId);
        Assert.Equal("Pending", changed.Name);
        Assert.Equal(EntityState.Modified, context.Entry(changed).State);
        Assert.Equal(
            rootId,
            context
                .Entry(changed)
                .Property(node => node.ParentId)
                .OriginalValue);
        Assert.True(
            context
                .Entry(changed)
                .Property(node => node.ParentId)
                .IsModified);
        Assert.Equal(
            "Branch",
            context
                .Entry(changed)
                .Property(node => node.Name)
                .OriginalValue);
        Assert.True(
            context
                .Entry(changed)
                .Property(node => node.Name)
                .IsModified);
    }

    /// <summary>Rolls back a completed mixed-subtype payload save when the enclosing delegate fails.</summary>
    /// <param name="tpt">Whether structural state uses a TPT base fragment.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedSubtypeSaveFailureRollsBackPayloadAndTracker(
        bool tpt
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(tpt);
        var rootId = tpt ? 12_001 : 13_001;
        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(tpt, false, rootId, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 1, "Folder"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 2, "Metric"), rootId, CancellationToken.None);
        var tracked = await context
            .Set<InheritanceNode>()
            .Where(node => node.TreeId == treeId)
            .ToArrayAsync(CancellationToken.None);

        var before = await PersistedAsync(context, treeId);
        var changed = tracked.Single(node => node.Id == rootId + 2);
        changed.ParentId = rootId + 1;
        changed.Name = "Pending";

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveNestedSetChangesAsync(
            true,
            async token =>
            {
                await context.SaveChangesAsync(false, token);

                throw new InjectedCommandException();
            },
            CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        var persisted = await PersistedAsync(context, treeId);
        Assert.Equal(before.Select(Snapshot), persisted.Select(Snapshot));
        Assert.Equal(rootId + 1, changed.ParentId);
        Assert.Equal("Pending", changed.Name);
        Assert.Equal(EntityState.Modified, context.Entry(changed).State);
        Assert.Equal(
            rootId,
            context
                .Entry(changed)
                .Property(node => node.ParentId)
                .OriginalValue);
        Assert.True(
            context
                .Entry(changed)
                .Property(node => node.ParentId)
                .IsModified);
        Assert.Equal(
            "Metric",
            context
                .Entry(changed)
                .Property(node => node.Name)
                .OriginalValue);
        Assert.True(
            context
                .Entry(changed)
                .Property(node => node.Name)
                .IsModified);
        Assert.Equal(
            before.Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position)),
            tracked
                .OrderBy(node => node.Left)
                .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position)));
    }

    /// <summary>Creates a provider context with a distinct table set for each inheritance contract.</summary>
    private async Task<DbContext> CreateInheritanceContextAsync(
        bool tpt,
        bool ordered = false
    ) => (tpt, ordered) switch
    {
        (false, false) =>
            await _fixture.CreateContextAsync<TphContext>(Engine, static options => new TphContext(options)),
        (true, false) =>
            await _fixture.CreateContextAsync<TptContext>(Engine, static options => new TptContext(options)),
        (false, true) => await _fixture.CreateContextAsync<OrderedTphContext>(
            Engine,
            static options => new OrderedTphContext(options)),
        (true, true) => await _fixture.CreateContextAsync<OrderedTptContext>(
            Engine,
            static options => new OrderedTptContext(options)),
    };

    /// <summary>Creates one concrete node with payload declared on the shared hierarchy base.</summary>
    private static InheritanceNode Node(
        bool tpt,
        bool metric,
        int id,
        string name
    )
    {
        InheritanceNode node = (tpt, metric) switch
        {
            (false, false) => new TphFolderNode(),
            (false, true) => new TphMetricNode(),
            (true, false) => new TptFolderNode(),
            (true, true) => new TptMetricNode(),
        };

        node.Id = id;
        node.Name = name;

        return node;
    }

    /// <summary>Reads authoritative coordinates without reusing tracked hierarchy instances.</summary>
    private static Task<InheritanceNode[]> PersistedAsync(
        DbContext context,
        Guid treeId
    ) => context
        .Set<InheritanceNode>()
        .AsNoTracking()
        .Where(node => node.TreeId == treeId)
        .OrderBy(node => node.Left)
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Compares the structural and payload state relevant to coordinated-save rollback.</summary>
    private static (int, Guid, int?, long, long, int, long, string) Snapshot(
        InheritanceNode node
    ) => (node.Id, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth, node.Position, node.Name);

    /// <summary>Checks current and original structure for every tracked derived node after bulk updates.</summary>
    private static void AssertTracked(
        DbContext context,
        InheritanceNode[] tracked,
        InheritanceNode[] persisted
    )
    {
        Assert.Equal(
            persisted
                .OrderBy(node => node.Id)
                .Select(Snapshot),
            tracked
                .OrderBy(node => node.Id)
                .Select(Snapshot));

        foreach (var node in tracked)
        {
            var entry = context.Entry(node);
            Assert.Equal(
                node.TreeId,
                entry.Property(value => value.TreeId).OriginalValue);
            Assert.Equal(
                node.ParentId,
                entry.Property(value => value.ParentId).OriginalValue);
            Assert.Equal(
                node.Left,
                entry.Property(value => value.Left).OriginalValue);
            Assert.Equal(
                node.Right,
                entry.Property(value => value.Right).OriginalValue);
            Assert.Equal(
                node.Depth,
                entry.Property(value => value.Depth).OriginalValue);
            Assert.Equal(
                node.Position,
                entry.Property(value => value.Position).OriginalValue);
            Assert.False(entry.Property(value => value.ParentId).IsModified);
            Assert.False(entry.Property(value => value.Left).IsModified);
            Assert.False(entry.Property(value => value.Right).IsModified);
            Assert.False(entry.Property(value => value.Depth).IsModified);
            Assert.False(entry.Property(value => value.Position).IsModified);
        }
    }
}
