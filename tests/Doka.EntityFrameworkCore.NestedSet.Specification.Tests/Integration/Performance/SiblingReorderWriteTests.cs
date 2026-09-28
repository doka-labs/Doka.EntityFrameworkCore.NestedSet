namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies permutation writes exclude stable subtrees, including islands inside changed roots.</summary>
public abstract partial class SiblingReorderWriteTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Shares provider provisioning while every scenario owns an independently seeded forest.</summary>
    protected SiblingReorderWriteTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Two separate swaps do not rewrite the large unchanged tree between them.</summary>
    [Fact]
    public async Task StableInteriorSubtreeIsExcludedFromEveryUpdate()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(
            setup,
            [1, 1, 257, 1, 1],
            ["B", "A", "C", "E", "D"]);

        var probe = new StructuralWriteProbe("OrderingNodes", "Left", "Right");
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);

        // Act
        await ReorderAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(8, probe.NodeUpdates.Sum(write => write.Rows));
        Assert.Equal(8, probe.BoundsWrites.Sum(write => write.Rows));
        var siblings = await SiblingsAsync(context);
        Assert.Equal([2, 1, 3, 5, 4], siblings);
        Assert.Equal(
            6,
            await context
                .Set<OrderingNode>()
                .Where(node => node.Id == 3)
                .Select(node => node.Left)
                .SingleAsync(CancellationToken.None));
        Assert.True(
            (await context
                .NestedSet<OrderingNode>()
                .ForScope(1)
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>A changed ordinal with unchanged bounds updates a sibling without rewriting its descendants.</summary>
    [Fact]
    public async Task PositionOnlySiblingDoesNotRewriteItsDescendants()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup, [1, 1, 257, 2], ["C", "D", "B", "A"]);
        var probe = new StructuralWriteProbe("OrderingNodes", "Left", "Right");
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);

        // Act
        await ReorderAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(9, probe.NodeUpdates.Sum(write => write.Rows));
        Assert.Equal(8, probe.BoundsWrites.Sum(write => write.Rows));
        var siblings = await SiblingsAsync(context);
        Assert.Equal([4, 3, 1, 2], siblings);
        var stable = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);
        Assert.Equal(6, stable.Left);
        Assert.Equal(1, stable.Position);
        Assert.True(
            (await context
                .NestedSet<OrderingNode>()
                .ForScope(1)
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>
    /// More than one parameter batch preserves the stable middle subtree and all moved leaf positions.
    /// </summary>
    [Fact]
    public async Task MultipleBatchesSkipTheStableInteriorSubtree()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var sizes = Enumerable
            .Repeat(1, 81)
            .ToArray();

        sizes[40] = 257;
        var names = Enumerable
            .Range(0, 81)
            .Select(index => $"Sibling-{index:D3}")
            .ToArray();

        for (var index = 0; index < 40; index += 2)
        {
            (names[index], names[index + 1]) = (names[index + 1], names[index]);
            (names[index + 41], names[index + 42]) = (names[index + 42], names[index + 41]);
        }

        await SeedAsync(setup, sizes, names);
        var probe = new StructuralWriteProbe("OrderingNodes", "Left", "Right");
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);

        // Act
        await ReorderAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(160, probe.NodeUpdates.Sum(write => write.Rows));
        Assert.Equal(4, probe.BoundsWrites.Count);
        Assert.Equal(
            82,
            await context
                .Set<OrderingNode>()
                .Where(node => node.Id == 41)
                .Select(node => node.Left)
                .SingleAsync(CancellationToken.None));
        Assert.True(
            (await context
                .NestedSet<OrderingNode>()
                .ForScope(1)
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Runs the real permutation writer inside the ordinary locked mutation boundary.</summary>
    private static Task ReorderAsync(
        OrderingContext context,
        CancellationToken token
    )
    {
        var store = new NestedSetStore<OrderingNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(OrderingNode))!,
            1,
            Guid.Empty);

        var executor = new NestedSetMutationExecutor<OrderingNode, int, Guid, int>(context, store.Map.EntityType);

        return executor.ExecuteAsync(
            async cancellation =>
            {
                await new NestedSetSiblingReorderer<OrderingNode, int, Guid, int>(store).ReorderAsync(
                    new NestedSetParent<int>(true, 9000001),
                    cancellation);
            },
            [store.LockRequest(NestedSetTreeLockMode.Existing)],
            token);
    }

    /// <summary>Reads only the measured child identities in their persisted hierarchy order.</summary>
    private static Task<int[]> SiblingsAsync(
        OrderingContext context
    ) => context
        .Set<OrderingNode>()
        .AsNoTracking()
        .Where(node => node.Scope == 1 && node.ParentId == 9000001)
        .OrderBy(node => node.Left)
        .Select(node => node.Id)
        .ToArrayAsync(CancellationToken.None);

    /// <summary>
    /// Seeds valid wide subtrees in physical input order, independently of their requested sibling order.
    /// </summary>
    private static async Task SeedAsync(
        OrderingContext context,
        int[] sizes,
        string[] names
    )
    {
        var nodes = new List<OrderingNode>();
        var boundary = 1;
        var childKey = 10000;

        for (var root = 0; root < sizes.Length; root++)
        {
            var left = boundary;
            var right = checked(left + (sizes[root] * 2) - 1);
            nodes.Add(
                new OrderingNode
                {
                    Id = root + 1,
                    Scope = 1,
                    Name = names[root],
                    Left = left,
                    Right = right,
                    Position = root,
                });

            for (var child = 0; child < sizes[root] - 1; child++)
            {
                nodes.Add(
                    new OrderingNode
                    {
                        Id = childKey++,
                        Scope = 1,
                        ParentId = root + 1,
                        Name = $"Child-{child:D4}",
                        Left = left + (child * 2) + 1,
                        Right = left + (child * 2) + 2,
                        Depth = 1,
                        Position = child,
                    });
            }

            boundary = right + 1;
        }

        // WHY: Reordering belongs to one sibling group inside one tree. The enclosing root keeps the original
        // subtree widths and every update budget unchanged while giving all measured siblings the same parent.
        foreach (var node in nodes)
        {
            node.Left++;
            node.Right++;
            node.Depth++;
            node.ParentId ??= 9000001;
        }

        nodes.Add(
            new OrderingNode
            {
                Id = 9000001,
                Scope = 1,
                Name = "Enclosing root",
                Left = 1,
                Right = boundary + 1,
            });

        nodes.Add(
            new OrderingNode
            {
                Id = 9000000,
                Scope = 2,
                Name = "Other scope",
                Left = 1,
                Right = 2
            });

        await context.AddRangeAsync(nodes, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);

        context.ChangeTracker.Clear();
    }
}
