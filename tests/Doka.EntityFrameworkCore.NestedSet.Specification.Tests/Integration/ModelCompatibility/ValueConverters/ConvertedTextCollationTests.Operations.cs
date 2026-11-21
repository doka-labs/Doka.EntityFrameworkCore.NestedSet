namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ConvertedTextCollationTests
{
    /// <summary>Deletion promotes aliased children and keeps the ordered sibling group dense.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task DeleteAsync<TKey, TParent>(
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        // Arrange
        var source = key("cHiLd");

        // Act
        await hierarchy.DeleteAsync(source, cancellationToken);

        // Assert
        var children = await hierarchy
            .ChildrenOf(key("rOoT"))
            .ToArrayAsync(cancellationToken);

        Assert.Equal(new[] { key("OTHER"), key("LEAF") }, children.Select(node => node.Id));
        Assert.Equal(new long[] { 0, 1 }, children.Select(node => node.Position));
        Assert.Equal(
            key("ROOT"),
            (await hierarchy
                .ParentOf(key("lEaF"))
                .SingleAsync(cancellationToken)).Id);

        await AssertValidAsync(hierarchy, treeId, cancellationToken: cancellationToken);
    }

    /// <summary>Managed alias changes preserve descendants and refresh unchanged tracked destination rows.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SaveAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Func<string, TParent> parent,
        Guid treeId,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        // Arrange
        var tracked = await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .ToArrayAsync(cancellationToken);

        var child = Assert.Single(tracked, node => node.Id.Equals(key("CHILD")));
        child.ParentId = parent("oThEr");
        child.Name = "Z";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            cancellationToken);

        // Assert
        Assert.Equal(
            key("OTHER"),
            (await hierarchy
                .ParentOf(key("cHiLd"))
                .SingleAsync(cancellationToken)).Id);

        Assert.Equal(
            key("CHILD"),
            (await hierarchy
                .ParentOf(key("lEaF"))
                .SingleAsync(cancellationToken)).Id);

        var stored = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(cancellationToken);

        Assert.All(
            tracked,
            node =>
            {
                var row = Assert.Single(stored, candidate => candidate.Id.Equals(node.Id));
                Assert.Equal(
                    (row.Left, row.Right, row.Depth, row.Position),
                    (node.Left, node.Right, node.Depth, node.Position));

                Assert.Equal(
                    EntityState.Unchanged,
                    context.Entry(node)
                        .State);
            });

        await AssertValidAsync(hierarchy, treeId, cancellationToken: cancellationToken);
    }

    /// <summary>Movement resolves aliased source and target groups using principal equality.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task MoveAsync<TKey, TParent>(
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        // Arrange
        var source = key("lEaF");
        var target = key("oThEr");

        // Act
        await hierarchy.MoveToAsync(source, target, cancellationToken);

        // Assert
        Assert.Empty(
            await hierarchy
                .ChildrenOf(key("cHiLd"))
                .ToArrayAsync(cancellationToken));

        Assert.Equal(
            key("LEAF"),
            (await hierarchy
                .ChildrenOf(key("oThEr"))
                .SingleAsync(cancellationToken)).Id);

        Assert.Equal(
            key("OTHER"),
            (await hierarchy
                .ParentOf(key("lEaF"))
                .SingleAsync(cancellationToken)).Id);

        await AssertValidAsync(hierarchy, treeId, cancellationToken: cancellationToken);
    }

    /// <summary>Repair and rule ordering resolve canonical parent links without losing nullable converters.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task MaintainAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        // Arrange
        await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .Where(node => node.Id.Equals(key("CHILD")))
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Name, "Z"), cancellationToken);

        // Act
        await hierarchy
            .InTree(treeId)
            .RebuildAsync(cancellationToken);

        // Assert
        var children = await hierarchy
            .ChildrenOf(key("rOoT"))
            .ToArrayAsync(cancellationToken);

        Assert.Equal(new[] { key("OTHER"), key("CHILD") }, children.Select(node => node.Id));
        Assert.Equal(
            key("CHILD"),
            (await hierarchy
                .ParentOf(key("lEaF"))
                .SingleAsync(cancellationToken)).Id);

        await AssertValidAsync(hierarchy, treeId, cancellationToken: cancellationToken);
    }

    /// <summary>Missing and cyclic alias requests preserve the entire persisted tree.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task RejectAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Guid treeId,
        bool missing,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        // Arrange
        var before = await SnapshotAsync<TKey, TParent>(context, cancellationToken: cancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<NestedSetException>(() => missing
            ? hierarchy.InsertChildAsync(
                new ConvertedTextCollationNode<TKey, TParent>
                {
                    Id = key("MISSING"),
                    Name = "Missing",
                },
                key("mIsSiNg"),
                cancellationToken)
            : hierarchy.MoveToAsync(key("rOoT"), key("lEaF"), cancellationToken));

        // Assert
        Assert.Equal(
            missing
                ? NestedSetErrorCode.NodeNotFound
                : NestedSetErrorCode.CycleDetected,
            failure.Code);

        Assert.Equal(before, await SnapshotAsync<TKey, TParent>(context, cancellationToken: cancellationToken));
        await AssertValidAsync(hierarchy, treeId, cancellationToken: cancellationToken);
    }

    /// <summary>A late callback failure restores rows and retains the caller's pending tracked change.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task RollbackAsync<TKey, TParent>(
        DbContext context,
        NestedSet<ConvertedTextCollationNode<TKey, TParent>> hierarchy,
        Func<string, TKey> key,
        Func<string, TParent> parent,
        Guid treeId,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        // Arrange
        var before = await SnapshotAsync<TKey, TParent>(context, cancellationToken: cancellationToken);
        var child = await context
            .Set<ConvertedTextCollationNode<TKey, TParent>>()
            .SingleAsync(node => node.Id.Equals(key("CHILD")), cancellationToken);

        child.ParentId = parent("oThEr");
        child.Name = "Z";
        var callbackRan = false;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveNestedSetChangesAsync(
            true,
            async token =>
            {
                await context.SaveChangesAsync(false, token);
                callbackRan = true;

                throw new InvalidOperationException("Converted collation rollback probe.");
            },
            cancellationToken));

        // Assert
        Assert.True(callbackRan);
        Assert.Equal("Converted collation rollback probe.", failure.Message);
        Assert.Equal(before, await SnapshotAsync<TKey, TParent>(context, cancellationToken: cancellationToken));
        Assert.Equal(parent("oThEr"), child.ParentId);
        Assert.Equal("Z", child.Name);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(child)
                .State);

        await AssertValidAsync(hierarchy, treeId, cancellationToken: cancellationToken);
    }
}
