namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class TypedParentContractTests
{
    /// <summary>Uses existing ValueGeneratedNever models without introducing a default-key sentinel.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task InsertAsync<TEntity, TKey>(
        string engine,
        TKey childKey,
        Func<TKey, TEntity> create,
        Func<TEntity, TKey?> parent,
        CancellationToken cancellationToken
    )
        where TEntity : class, IScopedNestedSetNode<TKey, Guid, int>
        where TKey : struct
    {
        // Arrange
        await using var context = await CreateContextAsync<TEntity>(engine, cancellationToken: cancellationToken);
        var hierarchy = context
            .NestedSet<TEntity>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(create(default), s_source, cancellationToken);
        var child = create(childKey);

        // Act
        await hierarchy.InsertChildAsync(child, default(TKey), cancellationToken);

        // Assert
        Assert.True(
            parent(child)
                .HasValue);

        Assert.Equal(
            default,
            parent(child)
                .GetValueOrDefault());

        Assert.Equal(
            default,
            (await hierarchy
                .ParentOf(childKey)
                .SingleAsync(cancellationToken)).Id);

        var stored = await hierarchy
            .InTree(s_source)
            .Nodes
            .ToArrayAsync(cancellationToken);

        var root = Assert.Single(stored, node => EqualityComparer<TKey>.Default.Equals(node.Id, default));
        Assert.False(
            parent(root)
                .HasValue);

        Assert.Equal(
            childKey,
            (await hierarchy
                .ChildrenOf(default(TKey))
                .SingleAsync(cancellationToken)).Id);

        Assert.Equal((1L, 4L, 0), (root.Left, root.Right, root.Depth));
        Assert.True(
            (await hierarchy
                .InTree(s_source)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);
    }

    /// <summary>Distinguishes a default-valued destination parent from null during a cross-tree move.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task ReparentAsync<TEntity, TKey>(
        string engine,
        TKey childKey,
        TKey otherRootKey,
        Func<TKey, TEntity> create,
        Func<TEntity, TKey?> parent,
        CancellationToken cancellationToken
    )
        where TEntity : class, IScopedNestedSetNode<TKey, Guid, int>
        where TKey : struct
    {
        // Arrange
        await using var context = await CreateContextAsync<TEntity>(engine, cancellationToken: cancellationToken);
        var hierarchy = context
            .NestedSet<TEntity>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(create(default), s_target, cancellationToken);
        await hierarchy.InsertRootAsync(create(otherRootKey), s_source, cancellationToken);
        await hierarchy.InsertChildAsync(create(childKey), otherRootKey, cancellationToken);

        // Act
        await hierarchy.MoveToAsync(childKey, default, cancellationToken);

        // Assert
        var child = await hierarchy
            .ChildrenOf(default(TKey))
            .SingleAsync(cancellationToken);

        Assert.Equal(childKey, child.Id);
        Assert.True(
            parent(child)
                .HasValue);

        Assert.Equal(
            default,
            parent(child)
                .GetValueOrDefault());

        Assert.Equal((s_target, 2L, 3L, 1), (child.TreeId, child.Left, child.Right, child.Depth));
        Assert.Equal(
            default,
            (await hierarchy
                .ParentOf(childKey)
                .SingleAsync(cancellationToken)).Id);

        Assert.Empty(
            await hierarchy
                .ChildrenOf(otherRootKey)
                .ToArrayAsync(cancellationToken));

        Assert.True(
            (await hierarchy
                .InTree(s_source)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);

        Assert.True(
            (await hierarchy
                .InTree(s_target)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);
    }

    /// <summary>Verifies parent absence is explicit when a typed subtree becomes a separate root.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task DetachAsync<TEntity, TKey>(
        string engine,
        TKey childKey,
        TKey grandchildKey,
        Func<TKey, TEntity> create,
        Func<TEntity, TKey?> parent,
        CancellationToken cancellationToken
    )
        where TEntity : class, IScopedNestedSetNode<TKey, Guid, int>
        where TKey : struct
    {
        // Arrange
        await using var context = await CreateContextAsync<TEntity>(engine, cancellationToken: cancellationToken);
        var hierarchy = context
            .NestedSet<TEntity>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(create(default), s_source, cancellationToken);
        await hierarchy.InsertChildAsync(create(childKey), default(TKey), cancellationToken);
        await hierarchy.InsertChildAsync(create(grandchildKey), childKey, cancellationToken);

        // Act
        await hierarchy.DetachAsTreeAsync(childKey, s_target, cancellationToken);

        // Assert
        var source = await hierarchy
            .InTree(s_source)
            .Nodes
            .SingleAsync(cancellationToken);

        Assert.Equal(default, source.Id);
        Assert.False(
            parent(source)
                .HasValue);

        Assert.Equal((1L, 2L, 0), (source.Left, source.Right, source.Depth));
        var detached = await hierarchy
            .InTree(s_target)
            .Nodes
            .ToArrayAsync(cancellationToken);

        Assert.Equal(2, detached.Length);
        Assert.False(
            parent(detached[0])
                .HasValue);

        Assert.True(
            parent(detached[1])
                .HasValue);

        Assert.Equal(
            childKey,
            parent(detached[1])
                .GetValueOrDefault());

        Assert.Empty(
            await hierarchy
                .ParentOf(childKey)
                .ToArrayAsync(cancellationToken));

        Assert.Empty(
            await hierarchy
                .ChildrenOf(default(TKey))
                .ToArrayAsync(cancellationToken));

        Assert.True(
            (await hierarchy
                .InTree(s_target)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);
    }
}
