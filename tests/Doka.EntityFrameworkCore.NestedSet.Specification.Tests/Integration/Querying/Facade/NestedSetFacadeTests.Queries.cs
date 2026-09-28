namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetFacadeTests
{
    /// <summary>All key-based queries resolve their anchor in SQL and retain stable hierarchy order.</summary>
    [Theory]
    [MemberData(nameof(QueryCases))]
    public async Task KeyQueryReturnsExpectedPreorder(
        string operation,
        int[] expected
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedScopedAsync(database);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var query = operation switch
        {
            "Tree" => hierarchy.TreeContaining(3),
            "Subtree" => hierarchy.SubtreeOf(2),
            "Children" => hierarchy.ChildrenOf(1),
            "Descendants" => hierarchy.DescendantsOf(1),
            "Ancestors" => hierarchy.AncestorsOf(3),
            "Parent" => hierarchy.ParentOf(3),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        // Act
        var actual = await query
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Scope and TreeId isolate trees that use identical interval coordinates.</summary>
    [Fact]
    public async Task TreeBindingIsolatesIdenticalBounds()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedScopedAsync(database);
        await using var context = database.CreateContext();

        // Act
        var ids = await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_firstTree)
            .Nodes
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2, 3, 4], ids);
    }

    /// <summary>A composable key query executes as one database command without preloading its anchor.</summary>
    [Fact]
    public async Task ComposedAnchorQueryUsesOneCommand()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedScopedAsync(database);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);

        // Act
        var ids = await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .AncestorsOf(3)
            .Where(node => node.Payload == "managed")
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2], ids);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>A missing visible anchor produces an empty composed query.</summary>
    [Fact]
    public async Task MissingAnchorReturnsEmptyQuery()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await SeedScopedAsync(database);
        await using var context = database.CreateContext();

        // Act
        var nodes = await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .SubtreeOf(842653)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(nodes);
    }
}
