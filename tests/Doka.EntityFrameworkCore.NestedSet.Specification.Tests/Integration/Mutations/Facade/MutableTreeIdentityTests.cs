namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies binary tree isolation and asynchronous caller-mutation safety on every provider.</summary>
public abstract partial class MutableTreeIdentityTests : ProviderTest
{
    private readonly BinaryIdentityFixture _fixture;

    /// <summary>Creates tests backed by isolated databases on the assembly-owned provider servers.</summary>
    protected MutableTreeIdentityTests(
        IProviderFixture<BinaryIdentityFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A caller cannot redirect detachment by mutating its TreeId while source resolution awaits.</summary>
    [Fact]
    public async Task DetachAsTreeAsync_BeforeSourceResolution_SnapshotsTarget()
    {
        // Arrange
        var barrier = new SourceResolutionBarrier();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            barrier);

        var hierarchy = context
            .NestedSet<BinaryTreeNode>()
            .ForScope(7);

        byte[] sourceTreeId = [1, 2, 3, 4];
        byte[] targetTreeId = [5, 6, 7, 8];
        var expectedTarget = targetTreeId.ToArray();
        await hierarchy.InsertRootAsync(
            new BinaryTreeNode { Id = 1 },
            sourceTreeId,
            TestContext.Current.CancellationToken);
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 2 }, 1, TestContext.Current.CancellationToken);
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 3 }, 2, TestContext.Current.CancellationToken);
        var operationCancellationToken = TestContext.Current.CancellationToken;
        barrier.Arm();

        // Act
        var detachment = hierarchy.DetachAsTreeAsync(2, targetTreeId, operationCancellationToken);
        await barrier.Attempted.Task.WaitAsync(operationCancellationToken);

        try
        {
            targetTreeId[0] = 9;
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await detachment;
        var detached = await hierarchy
            .InTree(expectedTarget)
            .Nodes
            .ToArrayAsync(operationCancellationToken);

        var redirectedRows = await hierarchy
            .InTree(targetTreeId)
            .Nodes
            .AnyAsync(operationCancellationToken);

        var registry = context
            .Model
            .GetEntityTypes()
            .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

        var registryIds = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Select(row => EF.Property<byte[]>(row, NestedSetTreeRegistryMetadata.TreeId))
            .ToArrayAsync(operationCancellationToken);

        // Assert
        Assert.Collection(
            detached,
            root => Assert.Equal((2, null, 1L, 4L, 0), (root.Id, root.ParentId, root.Left, root.Right, root.Depth)),
            child => Assert.Equal(
                (3, (int?)2, 2L, 3L, 1),
                (child.Id, child.ParentId, child.Left, child.Right, child.Depth)));

        Assert.All(detached, node => Assert.Equal(expectedTarget, node.TreeId));
        Assert.False(redirectedRows);
        Assert.Equal(2, registryIds.Length);
        Assert.Contains(registryIds, identity => identity.SequenceEqual(expectedTarget));
        Assert.DoesNotContain(registryIds, identity => identity.SequenceEqual(targetTreeId));
    }

    /// <summary>Binary predicates isolate mutations and filtered anchored queries across overlapping trees.</summary>
    [Fact]
    public async Task InsertChildAsync_Binary_StoresIsolateOverlappingTreeBounds()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context
            .NestedSet<BinaryTreeNode>()
            .ForScope(7);

        byte[] firstTree = [1, 2, 3, 4];
        byte[] secondTree = [5, 6, 7, 8];
        await hierarchy.InsertRootAsync(
            new BinaryTreeNode { Id = 1 },
            firstTree,
            TestContext.Current.CancellationToken);
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 2 }, 1, TestContext.Current.CancellationToken);
        await hierarchy.InsertRootAsync(
            new BinaryTreeNode { Id = 10 },
            secondTree,
            TestContext.Current.CancellationToken);
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 20 }, 10, TestContext.Current.CancellationToken);
        await context
            .NestedSet<BinaryTreeNode>()
            .ForScope(9)
            .InsertRootAsync(new BinaryTreeNode { Id = 100 }, firstTree, TestContext.Current.CancellationToken);

        var metadata = context.Model.FindEntityType(typeof(BinaryTreeNode))!;
        var store = new NestedSetStore<BinaryTreeNode, int, byte[], int>(context, metadata, 7, firstTree);

        // Act
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 3 }, 2, TestContext.Current.CancellationToken);
        var selected = await store
            .Nodes
            .OrderBy(node => node.Left)
            .Select(node => node.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        var filtered = await hierarchy
            .TreeContaining(3)
            .Where(node => node.Depth > 0)
            .Select(node => node.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        var otherTree = await hierarchy
            .InTree(secondTree)
            .Nodes
            .ToArrayAsync(TestContext.Current.CancellationToken);

        var otherScope = await context
            .NestedSet<BinaryTreeNode>()
            .ForScope(9)
            .InTree(firstTree)
            .Nodes
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1, 2, 3], selected);
        Assert.Equal([2, 3], filtered);
        Assert.Collection(
            otherTree,
            root => Assert.Equal((10, 1L, 4L, 0), (root.Id, root.Left, root.Right, root.Depth)),
            child => Assert.Equal((20, 2L, 3L, 1), (child.Id, child.Left, child.Right, child.Depth)));

        Assert.Equal((100, 1L, 2L, 0), (otherScope.Id, otherScope.Left, otherScope.Right, otherScope.Depth));
    }

    /// <summary>The post-lookup guard detects CLR edits introduced while the identity query awaits.</summary>
    [Fact]
    public async Task InsertChildAsync_ChangesDuringIdentityResolution_AreRejectedBeforeLocksAndWrites()
    {
        // Arrange
        var barrier = new SourceResolutionBarrier();
        var commands = new CommandProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            barrier,
            commands);

        var hierarchy = context
            .NestedSet<BinaryTreeNode>()
            .ForScope(7);

        byte[] treeId = [1, 2, 3, 4];
        await hierarchy.InsertRootAsync(new BinaryTreeNode { Id = 1 }, treeId, TestContext.Current.CancellationToken);
        await context
            .NestedSet<BinaryTreeNode>()
            .ForScope(9)
            .InsertRootAsync(new BinaryTreeNode { Id = 100 }, treeId, TestContext.Current.CancellationToken);

        var tracked = await context
            .Set<BinaryTreeNode>()
            .SingleAsync(node => node.Id == 100, TestContext.Current.CancellationToken);

        var operationCancellationToken = TestContext.Current.CancellationToken;
        commands.Reset();
        barrier.Arm();

        // Act
        var insertion = hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 2 }, 1, operationCancellationToken);
        await barrier.Attempted.Task.WaitAsync(operationCancellationToken);

        try
        {
            // WHY: No explicit state assignment or DetectChanges occurs here. The late mutation guard must
            // discover this ordinary CLR edit after the asynchronous lookup and before acquiring any locks.
            tracked.Payload = "changed during lookup";
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        var failure = await Record.ExceptionAsync(() => insertion);
        var commandCount = commands.CommandCount;
        var updates = commands.UpdateCount;
        var rows = await context
            .Set<BinaryTreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(operationCancellationToken);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(1, commandCount);
        Assert.Equal(0, updates);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(tracked)
                .State);

        Assert.Equal("changed during lookup", tracked.Payload);
        Assert.Collection(
            rows,
            source => Assert.Equal((1, 1L, 2L, "original"), (source.Id, source.Left, source.Right, source.Payload)),
            unrelated => Assert.Equal(
                (100, 1L, 2L, "original"),
                (unrelated.Id, unrelated.Left, unrelated.Right, unrelated.Payload)));
    }

    /// <summary>Changing source and destination arrays cannot redirect a move within the discovered tree.</summary>
    [Fact]
    public async Task MoveToAsync_MutableBinaryInputs_SnapshotsSourceAndDestinationKeys()
    {
        // Arrange
        var barrier = new BinaryKeyResolutionBarrier();
        await using var context = await _fixture.CreateKeyContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            barrier);

        var hierarchy = context
            .NestedSet<BinaryNode>()
            .ForScope(7);

        await SeedBinarySiblingsAsync(hierarchy, cancellationToken: TestContext.Current.CancellationToken);
        byte[] source = [2, 2];
        byte[] destination = [4, 4];
        var operationCancellationToken = TestContext.Current.CancellationToken;
        barrier.Arm();

        // Act
        var move = hierarchy.MoveToAsync(source, destination, operationCancellationToken);
        await barrier.WaitForResolutionAsync(move, operationCancellationToken);

        try
        {
            // WHY: Every alias stays in the same tree, so checking TreeId membership alone cannot detect
            // caller retargeting. The requested node and anchor must retain their original key bytes.
            source[0] = source[1] = 3;
            destination[0] = destination[1] = 1;
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await move;
        var nodes = await hierarchy
            .InTree(Guid.Empty)
            .Nodes
            .ToArrayAsync(operationCancellationToken);

        // Assert
        var intended = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([2, 2]));
        var alias = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([3, 3]));
        var intendedParent = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([4, 4]));
        Assert.Equal<byte>([4, 4], intended.ParentId!);
        Assert.Equal(2, intended.Depth);
        Assert.Equal<byte>([1, 1], alias.ParentId!);
        Assert.Equal(1, alias.Depth);
        Assert.Equal<byte>([1, 1], intendedParent.ParentId!);
        Assert.Equal(1, intendedParent.Depth);
    }

    /// <summary>Changing a binary parent argument cannot attach the new child to another same-tree sibling.</summary>
    [Fact]
    public async Task InsertChildAsync_MutableBinaryInput_SnapshotsParentKey()
    {
        // Arrange
        var barrier = new BinaryKeyResolutionBarrier();
        await using var context = await _fixture.CreateKeyContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            barrier);

        var hierarchy = context
            .NestedSet<BinaryNode>()
            .ForScope(7);

        await SeedBinarySiblingsAsync(hierarchy, cancellationToken: TestContext.Current.CancellationToken);
        byte[] parent = [2, 2];
        var inserted = new BinaryNode { Id = [5, 5] };
        var operationCancellationToken = TestContext.Current.CancellationToken;
        barrier.Arm();

        // Act
        var insertion = hierarchy.InsertChildAsync(inserted, parent, operationCancellationToken);
        await barrier.WaitForResolutionAsync(insertion, operationCancellationToken);

        try
        {
            parent[0] = parent[1] = 3;
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await insertion;
        var nodes = await hierarchy
            .InTree(Guid.Empty)
            .Nodes
            .ToArrayAsync(operationCancellationToken);

        // Assert
        var persisted = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([5, 5]));
        var alias = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([3, 3]));
        Assert.Equal<byte>([2, 2], inserted.ParentId!);
        Assert.Equal<byte>([2, 2], persisted.ParentId!);
        Assert.Equal(2, persisted.Depth);
        Assert.Equal(alias.Left + 1, alias.Right);
    }

    /// <summary>Changing a binary source argument cannot delete a different node with the same TreeId.</summary>
    [Fact]
    public async Task DeleteSubtreeAsync_MutableBinaryInput_SnapshotsSourceKey()
    {
        // Arrange
        var barrier = new BinaryKeyResolutionBarrier();
        await using var context = await _fixture.CreateKeyContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            barrier);

        var hierarchy = context
            .NestedSet<BinaryNode>()
            .ForScope(7);

        await SeedBinarySiblingsAsync(hierarchy, cancellationToken: TestContext.Current.CancellationToken);
        byte[] source = [2, 2];
        var operationCancellationToken = TestContext.Current.CancellationToken;
        barrier.Arm();

        // Act
        var deletion = hierarchy.DeleteSubtreeAsync(source, operationCancellationToken);
        await barrier.WaitForResolutionAsync(deletion, operationCancellationToken);

        try
        {
            source[0] = source[1] = 3;
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await deletion;
        var nodes = await hierarchy
            .InTree(Guid.Empty)
            .Nodes
            .ToArrayAsync(operationCancellationToken);

        // Assert
        Assert.Equal(3, nodes.Length);
        Assert.DoesNotContain(nodes, node => node.Id.SequenceEqual<byte>([2, 2]));
        var alias = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([3, 3]));
        Assert.Equal<byte>([1, 1], alias.ParentId!);
        Assert.Equal(1, alias.Depth);
    }
}
