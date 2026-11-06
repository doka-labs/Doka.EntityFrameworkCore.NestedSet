using Doka.EntityFrameworkCore.NestedSet.Features.Facade;

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
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // WHY: InTree rejects a wrong TreeId before maintenance dispatch. Probe the three binding methods
        // directly so each route proves its own generic guard before reaching the cached invoker or SQL.
        var binding = new NestedSetMutationBinding<TreeNode>(
            context,
            context.Model.FindEntityType(typeof(TreeNode))!).ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_tree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        var rowsBefore = await RowsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var registryBefore = await RegistryAsync(context, cancellationToken: TestContext.Current.CancellationToken);
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
        var error = await Record.ExceptionAsync(() => DispatchAsync(
            hierarchy,
            binding,
            input,
            route,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, probe.CommandCount);
        Assert.Equal(inputBefore, Snapshot(input));
        Assert.Equal(
            EntityState.Detached,
            context.Entry(input)
                .State);

        Assert.Equal(EntityState.Unchanged, tracked.State);
        Assert.Equal("unchanged application row", application.Value);
        Assert.Same(
            application,
            Assert.Single(context.ChangeTracker.Entries())
                .Entity);

        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = database.CreateContext();
        Assert.Equal(
            rowsBefore,
            await RowsAsync(
                verification,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            registryBefore,
            await RegistryAsync(
                verification,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Calls public mutations and direct maintenance bindings with mismatched key or tree arguments.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task DispatchAsync(
        ScopedNestedSet<TreeNode, int> hierarchy,
        NestedSetMutationBinding<TreeNode> binding,
        TreeNode input,
        string route,
        CancellationToken cancellationToken
    ) => route switch
    {
        "InsertChild" => hierarchy.InsertChildAsync(input, "1", cancellationToken),
        "InsertFirstChild" => hierarchy.InsertAsFirstChildAsync(input, "1", cancellationToken),
        "InsertLastChild" => hierarchy.InsertAsLastChildAsync(input, "1", cancellationToken),
        "InsertBefore" => hierarchy.InsertBeforeAsync(input, "2", cancellationToken),
        "InsertAfter" => hierarchy.InsertAfterAsync(input, "2", cancellationToken),
        "InsertSubtree" =>
            hierarchy.InsertSubtreeAsync(new NestedSetBranch<TreeNode>(input), "1", cancellationToken),
        "Move" => hierarchy.MoveToAsync("2", "1", cancellationToken),
        "MoveBefore" => hierarchy.MoveBeforeAsync("2", "1", cancellationToken),
        "MoveAfter" => hierarchy.MoveAfterAsync("2", "1", cancellationToken),
        "DetachSource" => hierarchy.DetachAsTreeAsync("2", Guid.NewGuid(), cancellationToken),
        "DetachTree" => hierarchy.DetachAsTreeAsync(2, "new-tree", cancellationToken),
        "DeleteSubtree" => hierarchy.DeleteSubtreeAsync("2", cancellationToken),
        "DeleteTree" => hierarchy.DeleteTreeAsync("tree", cancellationToken),
        "Purge" => hierarchy.PurgeTreeIdAsync("tree", cancellationToken),
        "Validate" => binding.ValidateTreeAsync("tree", NestedSetValidationLevel.Full, cancellationToken),
        "Plan" => binding.PlanRebuildAsync("tree", cancellationToken),
        "Rebuild" => binding.RebuildTreeAsync("tree", cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    /// <summary>Captures the persisted hierarchy in key order without enrolling rows in the tracker.</summary>
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
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<(long Revision, byte Lifecycle)> RegistryAsync(
        TreeContext context,
        CancellationToken cancellationToken
    )
    {
        var registry = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(TreeNode))!)
            .Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Select(row => new ValueTuple<long, byte>(
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .SingleAsync(cancellationToken);
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
