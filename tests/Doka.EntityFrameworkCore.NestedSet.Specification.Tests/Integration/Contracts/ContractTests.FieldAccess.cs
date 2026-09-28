namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ContractTests
{
    /// <summary>Verifies that inserting a root stores bounds through the configured backing fields.</summary>
    /// <returns>A task that completes when the persisted root has been verified.</returns>
    [Fact]
    public async Task RootInsertionRespectsConfiguredFieldAccess()
    {
        // Arrange
        await using var context = await _fixture.CreateFieldContextAsync(Engine);
        var tree = context
            .NestedSet<FieldNode>()
            .ForScope(1);
        var root = new FieldNode { Id = 1 };

        // Act
        await tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        var stored = await tree
            .InTree(Guid.Empty)
            .Nodes
            .Select(x => new
            {
                x.Left,
                x.Right,
                x.Depth,
                x.Position,
            })
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal((1L, 2L, 0, 0L), (stored.Left, stored.Right, stored.Depth, stored.Position));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Verifies that child insertion reads backing fields instead of decorated parent getters.</summary>
    /// <returns>A task that completes when the persisted parent and child have been verified.</returns>
    [Fact]
    public async Task ChildInsertionRespectsConfiguredFieldAccess()
    {
        // Arrange
        await using var context = await _fixture.CreateFieldContextAsync(Engine);
        var tree = context
            .NestedSet<FieldNode>()
            .ForScope(1);
        await tree.InsertRootAsync(new FieldNode { Id = 1 }, Guid.Empty, CancellationToken.None);
        var child = new FieldNode { Id = 2 };

        // Act
        await tree.InsertChildAsync(child, 1, cancellationToken: CancellationToken.None);
        var stored = await context
            .Set<FieldNode>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Left,
                x.Right,
                x.Depth,
                x.Position,
                x.ParentId,
            })
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_parentAndChild, stored.Select(x => (x.Left, x.Right, x.Depth, x.Position)));
        Assert.Equal(1, stored[1].ParentId);
    }

    /// <summary>Verifies that validation reads field-backed structural values instead of altered CLR getters.</summary>
    /// <returns>A task that completes when the field-backed hierarchy has been validated.</returns>
    [Fact]
    public async Task ValidationRespectsConfiguredFieldAccess()
    {
        // Arrange
        await using var context = await _fixture.CreateFieldContextAsync(Engine);
        await SeedFieldHierarchyAsync(context);
        var tree = context
            .NestedSet<FieldNode>()
            .ForScope(1);

        // Act
        var errors = await tree
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Empty(errors.Issues);
    }

    /// <summary>Verifies that root promotion reads the stored interval and depth from backing fields.</summary>
    /// <returns>A task that completes when both field-backed roots have been verified.</returns>
    [Fact]
    public async Task MoveRespectsConfiguredFieldAccess()
    {
        // Arrange
        await using var context = await _fixture.CreateFieldContextAsync(Engine);
        await SeedFieldHierarchyAsync(context);
        var tree = context
            .NestedSet<FieldNode>()
            .ForScope(1);
        var detachedTreeId = Guid.NewGuid();

        // Act
        await tree.DetachAsTreeAsync(2, detachedTreeId, CancellationToken.None);
        var stored = await context
            .Set<FieldNode>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Left,
                x.Right,
                x.Depth,
                x.Position,
                x.ParentId,
            })
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(s_twoRoots, stored.Select(x => (x.Left, x.Right, x.Depth, x.Position)));
        Assert.All(stored, node => Assert.Null(node.ParentId));
    }

    /// <summary>Verifies that rebuild repairs field-backed intervals for a hierarchy or separate roots.</summary>
    /// <param name="separateRoots">Whether the fixture contains two roots instead of a root and child.</param>
    /// <returns>A task that completes when the repaired field-backed values have been verified.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RebuildRespectsConfiguredFieldAccess(
        bool separateRoots
    )
    {
        // Arrange
        await using var context = await _fixture.CreateFieldContextAsync(Engine);
        await SeedFieldHierarchyAsync(context);
        var tree = context
            .NestedSet<FieldNode>()
            .ForScope(1);

        var detachedTreeId = Guid.NewGuid();

        if (separateRoots)
        {
            await tree.DetachAsTreeAsync(2, detachedTreeId, CancellationToken.None);
        }

        await context
            .Set<FieldNode>()
            .ExecuteUpdateAsync(
                x => x
                    .SetProperty(n => n.Left, 1)
                    .SetProperty(n => n.Right, 2)
                    .SetProperty(n => n.Depth, 99),
                CancellationToken.None);

        // Act
        await tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None);

        if (separateRoots)
        {
            await tree
                .InTree(detachedTreeId)
                .RebuildAsync(CancellationToken.None);
        }

        var stored = await context
            .Set<FieldNode>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Left,
                x.Right,
                x.Depth,
                x.Position,
                x.ParentId,
            })
            .ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            separateRoots ? s_twoRoots : s_parentAndChild,
            stored.Select(x => (x.Left, x.Right, x.Depth, x.Position)));
        Assert.Equal(separateRoots ? null : 1, stored[1].ParentId);
    }
}
