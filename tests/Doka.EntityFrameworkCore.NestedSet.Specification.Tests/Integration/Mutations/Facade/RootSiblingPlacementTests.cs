namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies every relative mutation rejects a same-tree root as its sibling anchor.</summary>
public abstract class RootSiblingPlacementTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses reusable engines while every scenario owns an independent database.</summary>
    /// <param name="fixture">The fixture provisioning supported relational engines.</param>
    protected RootSiblingPlacementTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Gets all supported engines paired with each relative insertion and movement operation.</summary>
    public static TheoryData<string> Cases
    {
        get
        {
            var cases = new TheoryData<string>();

            foreach (var operation in new[] { "InsertBefore", "InsertAfter", "MoveBefore", "MoveAfter" })
            {
                cases.Add(operation);
            }

            return cases;
        }
    }

    /// <summary>
    /// A root anchor cannot create a second root or detach an existing child through sibling placement.
    /// </summary>
    /// <param name="operation">The relative placement whose root anchor must be rejected.</param>
    /// <returns>
    /// A task that completes after verifying coordinates, registries and detached input remain unchanged.
    /// </returns>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RootSiblingPlacementPreservesEveryTreeAndInput(
        string operation
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 100 }, Guid.NewGuid(), CancellationToken.None);
        var input = new TreeNode
        {
            NodeId = 3,
            Payload = "detached input",
        };

        var inputBefore = Capture(input);
        var rowsBefore = await RowsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var registriesBefore = await RegistryRowsAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => ApplyAsync(hierarchy, input, operation,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(NestedSetErrorCode.OperationRejected, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(rowsBefore, await RowsAsync(context, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(registriesBefore, await RegistryRowsAsync(context,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(inputBefore, Capture(input));
        Assert.Equal(EntityState.Detached, context.Entry(input).State);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Dispatches one real public mutation against the existing root key.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task ApplyAsync(
        ScopedNestedSet<TreeNode, int> hierarchy,
        TreeNode input,
        string operation,
        CancellationToken cancellationToken
    ) => operation switch
    {
        "InsertBefore" => hierarchy.InsertBeforeAsync(input, 1, cancellationToken),
        "InsertAfter" => hierarchy.InsertAfterAsync(input, 1, cancellationToken),
        "MoveBefore" => hierarchy.MoveBeforeAsync(2, 1, cancellationToken),
        "MoveAfter" => hierarchy.MoveAfterAsync(2, 1, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    /// <summary>Reads every tree independently of the mutated facade's bound identity.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<NodeSnapshot[]> RowsAsync(
        TreeContext context,
        CancellationToken cancellationToken
    )
    {
        var rows = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.NodeId)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(Capture)
            .ToArray();
    }

    /// <summary>Captures all application and structural values on the detached input.</summary>
    private static NodeSnapshot Capture(
        TreeNode node
    ) => new(
        node.NodeId,
        node.TreeId,
        node.Tree,
        node.Start,
        node.End,
        node.Parent,
        node.Depth,
        node.Position,
        node.Payload);

    /// <summary>Reads every registry revision and lifecycle to detect hidden writes on a rejected mutation.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<RegistrySnapshot[]> RegistryRowsAsync(
        TreeContext context,
        CancellationToken cancellationToken
    )
    {
        var entityType = context.Model.FindEntityType(typeof(TreeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(entityType).Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .OrderBy(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId))
            .Select(row => new RegistrySnapshot(
                EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Stores all row values by value for exact rollback comparison.</summary>
    private sealed record NodeSnapshot(
        int Id,
        Guid TreeId,
        int Scope,
        long Left,
        long Right,
        int? Parent,
        int Depth,
        long Position,
        string? Payload
    );

    /// <summary>Stores the lifecycle state of each independent tree identity.</summary>
    private sealed record RegistrySnapshot(
        Guid TreeId,
        long Revision,
        byte Lifecycle
    );
}
