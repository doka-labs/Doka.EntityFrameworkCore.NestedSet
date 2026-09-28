namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies generated identities and sort defaults across bounded bulk insertion batches.</summary>
public abstract class BulkGeneratedTests : ProviderTest
{
    private readonly BulkGeneratedFixture _fixture;

    /// <summary>Uses an independently isolated generated-key model per database engine.</summary>
    protected BulkGeneratedTests(
        IProviderFixture<BulkGeneratedFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    ///     All generated parents exist before immediate FK updates and generated criteria determine final order.
    /// </summary>
    [Fact]
    public async Task GeneratedParentsAndSortValuesAreResolvedInOneSave()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var saves = 0;
        context.SavedChanges += (_, _) => saves++;
        var tree = context
            .NestedSet<BulkGeneratedNode>()
            .ForScope(7);

        var root = new BulkGeneratedNode { Name = "Root" };
        var generated = new BulkGeneratedNode();
        var zulu = new BulkGeneratedNode { Name = "Zulu" };
        var alpha = new BulkGeneratedNode { Name = "Alpha" };
        var leaf = new BulkGeneratedNode { Name = "Leaf" };
        var branch = new NestedSetBranch<BulkGeneratedNode>(
            root,
            [
                new NestedSetBranch<BulkGeneratedNode>(zulu),
                new NestedSetBranch<BulkGeneratedNode>(generated, [new NestedSetBranch<BulkGeneratedNode>(leaf)]),
                new NestedSetBranch<BulkGeneratedNode>(alpha),
            ]);

        // Act
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkGeneratedNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        // Assert
        Assert.Equal(1, saves);
        Assert.Equal("Generated", generated.Name);
        Assert.All(
            new[]
            {
                root,
                generated,
                zulu,
                alpha,
                leaf
            },
            node => Assert.True(node.Id > 0));
        Assert.Equal(root.Id, generated.ParentId);
        Assert.Equal(generated.Id, leaf.ParentId);
        Assert.Equal(
            new[]
            {
                root.Id,
                alpha.Id,
                generated.Id,
                leaf.Id,
                zulu.Id
            },
            await tree
                .TreeContaining(leaf.Id)
                .Select(node => node.Id)
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>
    ///     A failed final refresh rolls back inserted rows and restores library- and database-assigned values.
    /// </summary>
    [Fact]
    public async Task FailedRefreshRestoresGeneratedKeysAndDefaults()
    {
        // Arrange
        var probe = new BulkRefreshFailure("BulkGeneratedNodes");
        await using var context = await _fixture.ResetAsync(Engine, probe);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<BulkGeneratedNode>()
            .ForScope(7);

        var root = new BulkGeneratedNode
        {
            Scope = 17,
            Left = 31,
            Right = 32,
        };

        var child = new BulkGeneratedNode();
        var branch = new NestedSetBranch<BulkGeneratedNode>(root, [new NestedSetBranch<BulkGeneratedNode>(child)]);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkGeneratedNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.True(probe.ReachedRefresh);
        Assert.Equal((0, 17, 31, 32), (root.Id, root.Scope, root.Left, root.Right));
        Assert.Null(root.Name);
        Assert.Null(child.Name);
        Assert.Null(root.Details.Label);
        Assert.Null(child.Details.Label);
        Assert.Equal((0, 0), (root.Version, root.Details.Revision));
        Assert.Equal((0, 0), (child.Version, child.Details.Revision));
        Assert.Equal(0, child.Id);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>A failed import does not reverse application-owned payload changes made by save callbacks.</summary>
    [Fact]
    public async Task FailedImportRetainsApplicationPayloadCallbackChanges()
    {
        // Arrange
        var probe = new BulkRefreshFailure("BulkGeneratedNodes");
        await using var context = await _fixture.ResetAsync(Engine, probe);
        context.SavedChanges += (_, _) => probe.Inserted = true;
        var tree = context
            .NestedSet<BulkGeneratedNode>()
            .ForScope(7);

        var detail = new BulkGeneratedDetails { Label = "Original" };
        var root = new BulkGeneratedNode { History = [detail] };
        var originalList = root.History;
        var branch = new NestedSetBranch<BulkGeneratedNode>(
            root,
            [new NestedSetBranch<BulkGeneratedNode>(new BulkGeneratedNode())]);

        context.SavedChanges += (_, _) =>
        {
            detail.Label = "Changed";
            root.History.Add(new BulkGeneratedDetails { Label = "Additional" });
        };

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkGeneratedNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.Same(originalList, root.History);
        Assert.Same(detail, root.History[0]);
        Assert.Equal(2, root.History.Count);
        Assert.Equal("Changed", detail.Label);
        Assert.Equal(0, root.Id);
        Assert.Equal((0L, 0L, 0, 0L), (root.Left, root.Right, root.Depth, root.Position));
        Assert.Empty(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }
}
