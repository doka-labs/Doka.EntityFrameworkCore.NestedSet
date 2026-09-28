namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies typed mutation entries reject mismatched arguments before database work.</summary>
public abstract class MutationDispatchContractTests : ProviderTest
{
    private static readonly Guid s_tree = Guid.Parse("43000000-0000-0000-0000-000000000001");
    private readonly RelationalFixture _fixture;

    /// <summary>Uses existing provider resources while each argument probe receives isolated rows.</summary>
    /// <param name="fixture">The owner of the reusable relational databases.</param>
    protected MutationDispatchContractTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Provides each independently bound argument role on every supported provider.</summary>
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        string[] routes =
        [
            "InsertChild",
            "InsertFirstChild",
            "InsertLastChild",
            "InsertBefore",
            "InsertAfter",
            "InsertSubtree",
            "Move",
            "MoveBefore",
            "MoveAfter",
            "DetachSource",
            "DetachTree",
            "DeleteSubtree",
            "DeleteTree",
            "Purge",
            "Validate",
            "Plan",
            "Rebuild",
        ];

        foreach (var route in routes)
        {
            yield return new TheoryDataRow<string>(route);
        }
    }

    /// <summary>Invalid generic arguments preserve input, tracked application state and persisted trees.</summary>
    /// <param name="route">The operation and argument role whose type does not match the finalized model.</param>
    /// <returns>A task that completes after verifying early rejection and every observable state boundary.</returns>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task WrongArgumentTypeRejectsBeforeSqlAndPreservesState(
        string route
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // WHY: InTree rejects a wrong TreeId before maintenance dispatch. Probe the three binding methods
        // directly so each route proves its own generic guard before reaching the cached invoker or SQL.
        var binding = new Features.Facade.NestedSetMutationBinding<TreeNode>(
            context,
            context.Model.FindEntityType(typeof(TreeNode))!).ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_tree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        var rowsBefore = await RowsAsync(context);
        var registryBefore = await RegistryAsync(context);
        var input = new TreeNode
        {
            NodeId = 3,
            Tree = 17,
            TreeId = Guid.Empty,
            Parent = 19,
            Start = 71,
            End = 72,
            Depth = 23,
            Position = 29,
            Payload = "caller-owned",
        };

        var inputBefore = Snapshot(input);
        var application = new UnrelatedRow
        {
            Id = 99,
            Value = "unchanged application row",
        };

        var tracked = context.Attach(application);
        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(() => DispatchAsync(hierarchy, binding, input, route));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, probe.CommandCount);
        Assert.Equal(inputBefore, Snapshot(input));
        Assert.Equal(EntityState.Detached, context.Entry(input).State);
        Assert.Equal(EntityState.Unchanged, tracked.State);
        Assert.Equal("unchanged application row", application.Value);
        Assert.Same(application, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = database.CreateContext();
        Assert.Equal(rowsBefore, await RowsAsync(verification));
        Assert.Equal(registryBefore, await RegistryAsync(verification));
    }

    /// <summary>Calls public mutations and direct maintenance bindings with mismatched key or tree arguments.</summary>
    private static Task DispatchAsync(
        ScopedNestedSet<TreeNode, int> hierarchy,
        Features.Facade.NestedSetMutationBinding<TreeNode> binding,
        TreeNode input,
        string route
    ) => route switch
    {
        "InsertChild" => hierarchy.InsertChildAsync(input, "1", CancellationToken.None),
        "InsertFirstChild" => hierarchy.InsertAsFirstChildAsync(input, "1", CancellationToken.None),
        "InsertLastChild" => hierarchy.InsertAsLastChildAsync(input, "1", CancellationToken.None),
        "InsertBefore" => hierarchy.InsertBeforeAsync(input, "2", CancellationToken.None),
        "InsertAfter" => hierarchy.InsertAfterAsync(input, "2", CancellationToken.None),
        "InsertSubtree" =>
            hierarchy.InsertSubtreeAsync(new NestedSetBranch<TreeNode>(input), "1", CancellationToken.None),
        "Move" => hierarchy.MoveToAsync("2", "1", CancellationToken.None),
        "MoveBefore" => hierarchy.MoveBeforeAsync("2", "1", CancellationToken.None),
        "MoveAfter" => hierarchy.MoveAfterAsync("2", "1", CancellationToken.None),
        "DetachSource" => hierarchy.DetachAsTreeAsync("2", Guid.NewGuid(), CancellationToken.None),
        "DetachTree" => hierarchy.DetachAsTreeAsync(2, "new-tree", CancellationToken.None),
        "DeleteSubtree" => hierarchy.DeleteSubtreeAsync("2", CancellationToken.None),
        "DeleteTree" => hierarchy.DeleteTreeAsync("tree", CancellationToken.None),
        "Purge" => hierarchy.PurgeTreeIdAsync("tree", CancellationToken.None),
        "Validate" => binding.ValidateTreeAsync("tree", NestedSetValidationLevel.Full, CancellationToken.None),
        "Plan" => binding.PlanRebuildAsync("tree", CancellationToken.None),
        "Rebuild" => binding.RebuildTreeAsync("tree", CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    /// <summary>Captures the persisted hierarchy in key order without enrolling rows in the tracker.</summary>
    private static async Task<NodeSnapshot[]> RowsAsync(
        TreeContext context
    )
    {
        var rows = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        return rows
            .Select(Snapshot)
            .ToArray();
    }

    /// <summary>Captures application and structural values before a rejected generic dispatch.</summary>
    private static NodeSnapshot Snapshot(
        TreeNode node
    ) => new(
        node.NodeId,
        node.Tree,
        node.TreeId,
        node.Parent,
        node.Start,
        node.End,
        node.Depth,
        node.Position,
        node.Payload);

    /// <summary>Reads the registry revision and lifecycle independently of any invalid bound tree argument.</summary>
    private static Task<(long Revision, byte Lifecycle)> RegistryAsync(
        TreeContext context
    )
    {
        var registry = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(TreeNode))!).Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Select(row => new ValueTuple<long, byte>(
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .SingleAsync(CancellationToken.None);
    }

    /// <summary>Stores complete row values for exact comparison across argument-validation failures.</summary>
    private sealed record NodeSnapshot(
        int Id,
        int Scope,
        Guid TreeId,
        int? Parent,
        long Left,
        long Right,
        int Depth,
        long Position,
        string? Payload
    );
}
