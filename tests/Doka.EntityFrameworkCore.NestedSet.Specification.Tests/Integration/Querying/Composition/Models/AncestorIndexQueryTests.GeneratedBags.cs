namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Generated dictionary keys and index flags require no normalized sentinel values from consumers.</summary>
    /// <param name="bulk">Whether insertion uses the detached branch API.</param>
    /// <param name="nullKey">Whether the input explicitly contains a null key instead of omitting it.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IndexedGeneratedBagInsertionAcceptsMissingOrNullIdentity(bool bulk, bool nullKey)
    {
        // Arrange
        var tree = Guid.NewGuid();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexGeneratedBagContext>(Engine,
            static options => new AncestorIndexGeneratedBagContext(options));

        var hierarchy = context.NestedSet<Dictionary<string, object?>>(AncestorIndexGeneratedBagContext.Mapping);
        var root = GeneratedBag("root", nullKey);
        await hierarchy.InsertRootAsync(root, tree, CancellationToken.None);
        var rootKey = Assert.IsType<int>(root[NestedSetPropertyRoles.NodeKey]);
        var child = GeneratedBag("child", nullKey);
        var leaf = GeneratedBag("leaf", nullKey);

        // Act
        await InsertGeneratedBagAsync(hierarchy, child, leaf, rootKey, bulk);

        // Assert
        var childKey = Assert.IsType<int>(child[NestedSetPropertyRoles.NodeKey]);
        Assert.True(childKey > 0);
        Assert.NotEqual(rootKey, childKey);
        Assert.Equal(rootKey, child[NestedSetPropertyRoles.Parent]);
        Assert.Equal(bulk, Assert.IsType<bool>(child[GeneratedBagFlag(context, AncestorIndexGeneratedBagContext.Mapping)]));

        if (bulk)
        {
            Assert.Equal(childKey, leaf[NestedSetPropertyRoles.Parent]);
            Assert.True(Assert.IsType<int>(leaf[NestedSetPropertyRoles.NodeKey]) > 0);
        }

        Assert.Equal(bulk ? 3 : 2, await hierarchy.InTree(tree).Nodes.CountAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Accepted generated identities disappear on rollback when the original dictionary omitted them or stored null.</summary>
    /// <param name="bulk">Whether the failed operation imports a detached branch.</param>
    /// <param name="nullKey">Whether a null identity was explicitly supplied before tracking.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IndexedGeneratedBagLateFailureRestoresExactRawInput(bool bulk, bool nullKey)
    {
        // Arrange
        var tree = Guid.NewGuid();
        var failure = new BagSaveFailure();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexGeneratedBagContext>(Engine,
            static options => new AncestorIndexGeneratedBagContext(options), failure);

        var hierarchy = context.NestedSet<Dictionary<string, object?>>(AncestorIndexGeneratedBagContext.Mapping);
        var root = GeneratedBag("root", nullKey);
        await hierarchy.InsertRootAsync(root, tree, CancellationToken.None);
        var rootKey = Assert.IsType<int>(root[NestedSetPropertyRoles.NodeKey]);
        var child = GeneratedBag("child", nullKey);
        var leaf = GeneratedBag("leaf", nullKey);
        var beforeChild = new Dictionary<string, object?>(child, StringComparer.Ordinal);
        var beforeLeaf = new Dictionary<string, object?>(leaf, StringComparer.Ordinal);
        failure.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => InsertGeneratedBagAsync(hierarchy, child, leaf, rootKey, bulk));

        // Assert
        Assert.Equal(BagSaveFailure.Message, Assert.IsType<InvalidOperationException>(error).Message);
        Assert.Equal(beforeChild.OrderBy(pair => pair.Key), child.OrderBy(pair => pair.Key));
        Assert.Equal(beforeLeaf.OrderBy(pair => pair.Key), leaf.OrderBy(pair => pair.Key));
        Assert.False(Assert.IsType<bool>(root[GeneratedBagFlag(context, AncestorIndexGeneratedBagContext.Mapping)]));
        Assert.Equal(2L, root[NestedSetPropertyRoles.Right]);
        Assert.Equal(1, await hierarchy.InTree(tree).Nodes.CountAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The same detached dictionaries can be retried after an accepted generated identity was rolled back.</summary>
    /// <param name="bulk">Whether both attempts use the branch import API.</param>
    /// <param name="nullKey">Whether the caller uses an explicit null identity.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IndexedGeneratedBagCanRetryAfterLateFailure(bool bulk, bool nullKey)
    {
        // Arrange
        var tree = Guid.NewGuid();
        var failure = new BagSaveFailure();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexGeneratedBagContext>(Engine,
            static options => new AncestorIndexGeneratedBagContext(options), failure);

        var hierarchy = context.NestedSet<Dictionary<string, object?>>(AncestorIndexGeneratedBagContext.Mapping);
        var root = GeneratedBag("root", nullKey);
        await hierarchy.InsertRootAsync(root, tree, CancellationToken.None);
        var rootKey = Assert.IsType<int>(root[NestedSetPropertyRoles.NodeKey]);
        var child = GeneratedBag("child", nullKey);
        var leaf = GeneratedBag("leaf", nullKey);
        failure.Enabled = true;
        var setupError = await Record.ExceptionAsync(() => InsertGeneratedBagAsync(hierarchy, child, leaf, rootKey, bulk));
        Assert.Equal(BagSaveFailure.Message, Assert.IsType<InvalidOperationException>(setupError).Message);
        failure.Enabled = false;

        // Act
        await InsertGeneratedBagAsync(hierarchy, child, leaf, rootKey, bulk);

        // Assert
        var childKey = Assert.IsType<int>(child[NestedSetPropertyRoles.NodeKey]);
        Assert.True(childKey > 0);
        Assert.Equal(rootKey, child[NestedSetPropertyRoles.Parent]);
        Assert.Equal(bulk, Assert.IsType<bool>(child[GeneratedBagFlag(context, AncestorIndexGeneratedBagContext.Mapping)]));
        Assert.Equal(bulk ? 3 : 2, await hierarchy.InTree(tree).Nodes.CountAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Preserves the exact consumer distinction between an absent and an explicitly null generated key.</summary>
    private static Dictionary<string, object?> GeneratedBag(string label, bool nullKey)
    {
        var node = new Dictionary<string, object?> { [AncestorIndexMappedContext.Payload] = label };

        if (nullKey)
        {
            node.Add(NestedSetPropertyRoles.NodeKey, null);
        }

        return node;
    }

    /// <summary>Keeps single and bulk invocations identical across success, restoration and retry contracts.</summary>
    private static Task InsertGeneratedBagAsync(
        NestedSet<Dictionary<string, object?>> hierarchy,
        Dictionary<string, object?> child,
        Dictionary<string, object?> leaf,
        int rootKey,
        bool bulk
    ) => bulk
        ? hierarchy.InsertSubtreeAsync(new NestedSetBranch<Dictionary<string, object?>>(
            child, [new NestedSetBranch<Dictionary<string, object?>>(leaf)]), rootKey, CancellationToken.None)
        : hierarchy.InsertChildAsync(child, rootKey, CancellationToken.None);
}
