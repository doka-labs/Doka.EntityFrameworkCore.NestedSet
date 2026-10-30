namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Public insertion paths compute and refresh required generated indexer values omitted by the caller.</summary>
    /// <param name="bulk">Whether one detached branch is inserted through the bulk API.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedPropertyBagInsertionDoesNotRequireCallerGeneratedFlag(bool bulk)
    {
        // Arrange
        var first = Interlocked.Add(ref s_mappingIdentity, 10);
        var tree = Guid.NewGuid();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexMappedContext>(Engine,
            static options => new AncestorIndexMappedContext(options));

        var hierarchy = context.NestedSet<Dictionary<string, object?>>(AncestorIndexMappedContext.First);
        await hierarchy.InsertRootAsync(Bag(first, "root"), tree, CancellationToken.None);
        var child = Bag(first + 1, "child");
        var leaf = Bag(first + 2, "leaf");
        var flag = GeneratedBagFlag(context);

        // Act
        if (bulk)
        {
            await hierarchy.InsertSubtreeAsync(new NestedSetBranch<Dictionary<string, object?>>(
                child, [new NestedSetBranch<Dictionary<string, object?>>(leaf)]), first, CancellationToken.None);
        }
        else
        {
            await hierarchy.InsertChildAsync(child, first, CancellationToken.None);
        }

        // Assert
        Assert.Equal(bulk, Assert.IsType<bool>(child[flag]));

        if (bulk)
        {
            Assert.False(Assert.IsType<bool>(leaf[flag]));
        }

        var actual = await hierarchy.InTree(tree).Nodes.Select(node => new
        {
            Key = EF.Property<int>(node, NestedSetPropertyRoles.NodeKey),
            NonLeaf = EF.Property<bool>(node, flag),
        }).ToArrayAsync(CancellationToken.None);

        Assert.Equal(bulk ? 3 : 2, actual.Length);
        Assert.Equal(bulk ? 2 : 1, actual.Count(node => node.NonLeaf));
        Assert.True(Assert.Single(actual, node => node.Key == first).NonLeaf);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Ordinary payload saving regenerates the ignored indexer even when an application omitted its prior value.</summary>
    [Fact]
    public async Task IndexedPropertyBagPayloadSaveRegeneratesOmittedFlag()
    {
        // Arrange
        var first = Interlocked.Add(ref s_mappingIdentity, 10);
        var tree = Guid.NewGuid();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexMappedContext>(Engine,
            static options => new AncestorIndexMappedContext(options));

        var hierarchy = context.NestedSet<Dictionary<string, object?>>(AncestorIndexMappedContext.First);
        await hierarchy.InsertRootAsync(Bag(first, "before"), tree, CancellationToken.None);
        await hierarchy.InsertChildAsync(Bag(first + 1, "child"), first, CancellationToken.None);
        var root = await context.Set<Dictionary<string, object?>>(AncestorIndexMappedContext.First)
            .SingleAsync(node => EF.Property<int>(node, NestedSetPropertyRoles.NodeKey) == first, CancellationToken.None);

        var flag = GeneratedBagFlag(context);
        root[AncestorIndexMappedContext.Payload] = "after";
        root.Remove(flag);

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal("after", root[AncestorIndexMappedContext.Payload]);
        Assert.True(Assert.IsType<bool>(root[flag]));
        Assert.Equal(1L, root[NestedSetPropertyRoles.Left]);
        Assert.Equal(4L, root[NestedSetPropertyRoles.Right]);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    /// <summary>Late insertion failures restore generated flag values and preserve originally absent dictionary entries.</summary>
    /// <param name="bulk">Whether the failed public operation imports a detached branch.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedPropertyBagRollbackRestoresCallerValuesAndMissingFlag(bool bulk)
    {
        // Arrange
        var first = Interlocked.Add(ref s_mappingIdentity, 10);
        var tree = Guid.NewGuid();
        var failure = new BagSaveFailure();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexMappedContext>(Engine,
            static options => new AncestorIndexMappedContext(options), failure);

        var hierarchy = context.NestedSet<Dictionary<string, object?>>(AncestorIndexMappedContext.First);
        var root = Bag(first, "root");
        await hierarchy.InsertRootAsync(root, tree, CancellationToken.None);
        var child = Bag(first + 1, "child");
        var leaf = Bag(first + 2, "leaf");
        var beforeChild = new Dictionary<string, object?>(child, StringComparer.Ordinal);
        var beforeLeaf = new Dictionary<string, object?>(leaf, StringComparer.Ordinal);
        var flag = GeneratedBagFlag(context);
        failure.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => bulk
            ? hierarchy.InsertSubtreeAsync(new NestedSetBranch<Dictionary<string, object?>>(
                child, [new NestedSetBranch<Dictionary<string, object?>>(leaf)]), first, CancellationToken.None)
            : hierarchy.InsertChildAsync(child, first, CancellationToken.None));

        // Assert
        Assert.Equal(BagSaveFailure.Message, Assert.IsType<InvalidOperationException>(error).Message);
        Assert.Equal(beforeChild.OrderBy(pair => pair.Key), child.OrderBy(pair => pair.Key));
        Assert.Equal(beforeLeaf.OrderBy(pair => pair.Key), leaf.OrderBy(pair => pair.Key));
        Assert.False(child.ContainsKey(flag));
        Assert.False(Assert.IsType<bool>(root[flag]));
        Assert.Equal(1L, root[NestedSetPropertyRoles.Left]);
        Assert.Equal(2L, root[NestedSetPropertyRoles.Right]);
        Assert.Equal(1, await hierarchy.InTree(tree).Nodes.CountAsync(CancellationToken.None));
    }

    /// <summary>Creates detached consumer data without any generated or managed structural values.</summary>
    private static Dictionary<string, object?> Bag(int key, string label) => new()
    {
        [NestedSetPropertyRoles.NodeKey] = key,
        [AncestorIndexMappedContext.Payload] = label,
    };

    /// <summary>Gets the exact generated property selected by the finalized named mapping.</summary>
    private static string GeneratedBagFlag(DbContext context, string mapping = AncestorIndexMappedContext.First)
        => NestedSetModelMapping.For(context.Model)
            .Descriptor(context.Model.FindEntityType(mapping)!)
            .AncestorIndexProperty?.Name ?? throw new InvalidOperationException("The generated property is missing.");

    /// <summary>Fails after EF accepts saved values so rollback must restore both tracker and dictionary shape.</summary>
    private sealed class BagSaveFailure : SaveChangesInterceptor
    {
        internal const string Message = "Injected indexed property-bag save failure.";

        /// <summary>Gets or sets whether the next completed payload save must fail.</summary>
        internal bool Enabled { get; set; }

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Enabled)
            {
                throw new InvalidOperationException(Message);
            }

            return ValueTask.FromResult(result);
        }
    }
}
