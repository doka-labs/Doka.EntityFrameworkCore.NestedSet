namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class InheritanceTests
{
    /// <summary>Moves a concrete metric under a folder through their complete base hierarchy.</summary>
    /// <param name="tpt">Whether structural state uses a TPT base fragment.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveNestedSetChangesAsync_MixedSubtypeParentChange_QueriesConfiguredOwner(
        bool tpt
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(
            tpt,
            cancellationToken: TestContext.Current.CancellationToken,
            ordered: false);

        var rootId = tpt
            ? 4_001
            : 5_001;

        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(tpt, false, rootId, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 1, "Folder"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 2, "Metric"), rootId, CancellationToken.None);
        var tracked = await context
            .Set<InheritanceNode>()
            .Where(node => node.TreeId == treeId)
            .ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Id == rootId + 2)
            .ParentId = rootId + 1;

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        var persisted = await PersistedAsync(context, treeId, cancellationToken: TestContext.Current.CancellationToken);
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
    public async Task SaveNestedSetChangesAsync_MixedSubtypeCrossTree_MovesShareDependenciesAndRefreshTracker(
        bool tpt
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(
            tpt,
            cancellationToken: TestContext.Current.CancellationToken,
            ordered: false);

        var rootId = tpt
            ? 6_001
            : 7_001;

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

        tracked.Single(node => node.Id == rootId + 1)
            .ParentId = rootId + 4;

        tracked.Single(node => node.Id == rootId + 5)
            .ParentId = rootId + 2;

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        var source = await PersistedAsync(
            context,
            sourceTree,
            cancellationToken: TestContext.Current.CancellationToken);

        var target = await PersistedAsync(
            context,
            targetTree,
            cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task SaveNestedSetChangesAsync_BaseOrdering_RefreshesMixedSubtypeSubtrees(
        bool tpt,
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(
            tpt,
            ordered: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var rootId = (tpt
                ? 8_001
                : 9_001)
            + (acceptAllChangesOnSuccess
                ? 100
                : 0);

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
        var persisted = await PersistedAsync(context, treeId, cancellationToken: TestContext.Current.CancellationToken);
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
        var state = acceptAllChangesOnSuccess
            ? EntityState.Unchanged
            : EntityState.Modified;

        Assert.Equal(
            state,
            context.Entry(folder)
                .State);

        Assert.Equal(
            state,
            context.Entry(metric)
                .State);

        Assert.Equal(
            acceptAllChangesOnSuccess
                ? "Zulu"
                : "Bravo",
            context
                .Entry(folder)
                .Property(node => node.Name)
                .OriginalValue);

        Assert.Equal(
            acceptAllChangesOnSuccess
                ? "Alpha"
                : "Charlie",
            context
                .Entry(metric)
                .Property(node => node.Name)
                .OriginalValue);

        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Creates a provider context with a distinct table set for each inheritance contract.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task<DbContext> CreateInheritanceContextAsync(
        bool tpt,
        bool ordered,
        CancellationToken cancellationToken
    ) => (tpt, ordered) switch
    {
        (false, false) =>
            await _fixture.CreateContextAsync<TphContext>(
                Engine,
                static options => new TphContext(options),
                cancellationToken: cancellationToken),
        (true, false) =>
            await _fixture.CreateContextAsync<TptContext>(
                Engine,
                static options => new TptContext(options),
                cancellationToken: cancellationToken),
        (false, true) => await _fixture.CreateContextAsync<OrderedTphContext>(
            Engine,
            static options => new OrderedTphContext(options),
            cancellationToken: cancellationToken),
        (true, true) => await _fixture.CreateContextAsync<OrderedTptContext>(
            Engine,
            static options => new OrderedTptContext(options),
            cancellationToken: cancellationToken),
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
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<InheritanceNode[]> PersistedAsync(
        DbContext context,
        Guid treeId,
        CancellationToken cancellationToken
    ) => context
        .Set<InheritanceNode>()
        .AsNoTracking()
        .Where(node => node.TreeId == treeId)
        .OrderBy(node => node.Left)
        .ToArrayAsync(cancellationToken);

    /// <summary>Compares the structural and payload state relevant to coordinated-save rollback.</summary>
    private static (int, Guid, int?, long, long, int, long, string) Snapshot(
        InheritanceNode node
    ) => (node.Id, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth, node.Position, node.Name);
}
