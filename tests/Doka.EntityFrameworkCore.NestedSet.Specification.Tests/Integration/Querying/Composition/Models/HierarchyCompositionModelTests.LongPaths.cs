namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Long converted-key paths preserve nullable Parent joins and reject an interrupted chain.</summary>
    /// <param name="filter">The independent, endpoint-correlated, or nearest path contract.</param>
    /// <param name="missingParent">Whether an intermediate visible node lacks its required Parent.</param>
    [Theory]
    [InlineData("independent", false)]
    [InlineData("independent", true)]
    [InlineData("correlated", false)]
    [InlineData("correlated", true)]
    [InlineData("nearest", false)]
    [InlineData("nearest", true)]
    public async Task LongConvertedKeyPathsRetainNullableParentIdentity(
        string filter,
        bool missingParent
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<CompositionConvertedContext>(Engine,
            static options => new CompositionConvertedContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        await context.AddRangeAsync(Enumerable.Range(0, 6).Select(depth => new CompositionConvertedNode
        {
            Scope = scope, TreeId = tree, Id = new CompositionNodeKey(depth),
            ParentId = depth == 0 || (missingParent && depth == 3) ? null : new CompositionNodeKey(depth - 1),
            Left = depth + 1, Right = 12 - depth, Depth = depth,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        materialized = 0;
        var hierarchy = context.NestedSet<CompositionConvertedNode>().ForScope(scope).InTree(tree);
        int[] expected = missingParent ? [] : filter == "correlated" ? [1] : [0, 1];

        // Act
        var actual = await QueryLongDepths(hierarchy, filter)
            .OrderBy(depth => depth).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, materialized);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Long paths through a secondary structural fragment retain complete Parent and payload identity.
    /// </summary>
    /// <param name="filter">The independent, endpoint-correlated, or nearest path contract.</param>
    /// <param name="missingParent">Whether an intermediate visible fragment lacks its required Parent.</param>
    [Theory]
    [InlineData("independent", false)]
    [InlineData("independent", true)]
    [InlineData("correlated", false)]
    [InlineData("correlated", true)]
    [InlineData("nearest", false)]
    [InlineData("nearest", true)]
    public async Task LongEntitySplitPathsRetainStructuralFragmentIdentity(
        string filter,
        bool missingParent
    )
    {
        // Arrange
        var first = NextIdentity();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await _fixture.CreateContextAsync<EntitySplitContext>(Engine,
            static options => new EntitySplitContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        await context.AddRangeAsync(Enumerable.Range(0, 6).Select(depth => new EntitySplitNode
        {
            Id = first + depth, TreeId = tree, Name = $"Long fragment {depth}",
            ParentId = depth == 0 || (missingParent && depth == 3) ? null : first + depth - 1,
            Left = depth + 1, Right = 12 - depth, Depth = depth,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        materialized = 0;
        var hierarchy = context.NestedSet<EntitySplitNode>().InTree(tree);
        int[] expected = missingParent ? [] : filter == "correlated" ? [1] : [0, 1];

        // Act
        var actual = await QueryLongDepths(hierarchy, filter)
            .OrderBy(depth => depth).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, materialized);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Exercises distance-five and distance-four paths using each model's real mapped key transport.</summary>
    /// <typeparam name="TEntity">The actual mapped node type, including named or shadow-backed models.</typeparam>
    /// <param name="hierarchy">The exact selected tree and optional scope.</param>
    /// <param name="filter">The independent, endpoint-correlated, or nearest path contract.</param>
    /// <param name="depthProperty">The configured depth role, independent of misleading conventional CLR members.</param>
    private static IQueryable<int> QueryLongDepths<TEntity>(
        NestedSetTree<TEntity, Guid> hierarchy,
        string filter,
        string depthProperty = nameof(INestedSetNode<int, Guid>.Depth)
    )
        where TEntity : class
    {
        var rows = hierarchy.SelectSubtrees(node => EF.Property<int>(node, depthProperty) <= 1,
                (upper, node, distance) => new { Upper = upper, Node = node })
            .Where(row => EF.Property<int>(row.Node, depthProperty) == 5);

        // WHY: Different upper endpoints provide distinct row-dependent limits without introducing CLR
        // operators on the converted key. Neither path proof may assume its parent key is non-nullable.
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => true),
            "correlated" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Node,
                (row, parent, child) => EF.Property<int>(child, depthProperty)
                    <= EF.Property<int>(row.Upper, depthProperty) + 4),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => EF.Property<int>(owner, depthProperty) == 0, (row, owner) => true),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        return query.Select(row => EF.Property<int>(row.Upper, depthProperty));
    }
}
