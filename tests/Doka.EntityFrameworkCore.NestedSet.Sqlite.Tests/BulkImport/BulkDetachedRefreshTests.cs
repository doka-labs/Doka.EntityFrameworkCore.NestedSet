using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies that the final bulk refresh does not retain detached input entities in the context.</summary>
[Collection("Allocation measurements")]
public sealed class BulkDetachedRefreshTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>,
    IClassFixture<ProviderFixture<BulkGeneratedFixture, SqliteEngine>>
{
    private readonly RelationalFixture _fixture;
    private readonly BulkGeneratedFixture _generated;

    /// <summary>Uses the SQLite fixture owning the isolated database.</summary>
    /// <param name="fixture">The provider fixture owned by this suite.</param>
    /// <param name="generated">The generated-value fixture owned by this suite.</param>
    public BulkDetachedRefreshTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture,
        ProviderFixture<BulkGeneratedFixture, SqliteEngine> generated
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _generated = generated.Value;
    }

    /// <summary>Completed imports release their input objects while unrelated tracked state remains intact.</summary>
    [Fact]
    public async Task CompletedImportDoesNotRetainDetachedInputs()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var marker = new UnrelatedRow { Id = 500_000 };
        context.Attach(marker);

        // Act
        var inputs = await ImportWithoutRetainingInputsAsync(context);

        // Assert
        // WHY: Entries() omits EF's detached reference map. Collection while the context stays alive detects
        // hidden entry retention that an empty tracked-entries assertion cannot expose.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(inputs, input => Assert.False(input.TryGetTarget(out _)));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(129, await context.Set<TreeNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Generated-value rollback releases input objects after restoring their original values.</summary>
    [Fact]
    public async Task RolledBackGeneratedImportDoesNotRetainDetachedInputs()
    {
        // Arrange
        var probe = new BulkRefreshFailure("BulkGeneratedNodes");
        await using var context = await _generated.ResetAsync(Engine, probe);
        context.SavedChanges += (_, _) => probe.Inserted = true;

        // Act
        var result = await FailImportWithoutRetainingInputsAsync(context);

        // Assert
        Assert.IsType<InjectedCommandException>(result.Error);
        Assert.True(probe.ReachedRefresh);
        Assert.True(result.Restored);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(result.Inputs, input => Assert.False(input.TryGetTarget(out _)));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0, await context.Set<BulkGeneratedNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Failure leaves generated caller values untouched on inputs that never entered a batch.</summary>
    /// <param name="beforeStaging">Whether an existing tree rejects reservation before the first payload batch.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlyFailurePreservesUnstagedGeneratedInputs(
        bool beforeStaging
    )
    {
        // Arrange
        await using var context = await _generated.ResetAsync(Engine);
        var hierarchy = context
            .NestedSet<BulkGeneratedNode>()
            .ForScope(7);

        if (beforeStaging)
        {
            await hierarchy.InsertRootAsync(
                new BulkGeneratedNode { Name = "Existing root" },
                Guid.Empty,
                CancellationToken.None);
        }

        var nodes = Enumerable
            .Range(1, 130)
            .Select(index => new BulkGeneratedNode
            {
                Name = $"Original {index:D3}",
                Version = 19,
                Details = new BulkGeneratedDetails
                {
                    Label = "Original detail",
                    Revision = 23,
                },
            })
            .ToArray();

        var branch = new NestedSetBranch<BulkGeneratedNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<BulkGeneratedNode>(node))
                .ToArray());

        var completedSaves = 0;
        context.SavedChanges += (_, _) =>
        {
            completedSaves++;

            throw new InjectedCommandException();
        };

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertForestAsync(
            [new NestedSetTreeImport<BulkGeneratedNode, Guid>(Guid.Empty, branch)],
            CancellationToken.None));

        // Assert
        if (beforeStaging)
        {
            Assert.Equal(
                NestedSetErrorCode.TreeIdUnavailable,
                Assert.IsType<NestedSetException>(error).Code);
        }
        else
        {
            Assert.IsType<InjectedCommandException>(error);
        }

        Assert.Equal(beforeStaging ? 0 : 1, completedSaves);
        Assert.All(
            nodes.Select((node, index) => (node, index)),
            item =>
            {
                Assert.Equal($"Original {item.index + 1:D3}", item.node.Name);
                Assert.Equal("Original detail", item.node.Details.Label);
                Assert.Equal((19, 23), (item.node.Version, item.node.Details.Revision));
                Assert.Equal(0, item.node.Id);
            });

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(
            beforeStaging ? 1 : 0,
            await context
                .Set<BulkGeneratedNode>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Keeps input references inside a completed helper so JIT liveness cannot affect collection.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<TreeNode>[]> ImportWithoutRetainingInputsAsync(
        TreeContext context
    )
    {
        var nodes = Enumerable
            .Range(1, 129)
            .Select(key => new TreeNode { NodeId = key })
            .ToArray();

        var branch = new NestedSetBranch<TreeNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<TreeNode>(node))
                .ToArray());

        await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InsertForestAsync([new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, branch)], CancellationToken.None);

        return nodes
            .Select(node => new WeakReference<TreeNode>(node))
            .ToArray();
    }

    /// <summary>Returns rollback evidence without retaining generated inputs in the caller's async frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<( WeakReference<BulkGeneratedNode>[] Inputs, Exception? Error, bool Restored)>
        FailImportWithoutRetainingInputsAsync(
            BulkGeneratedContext context
        )
    {
        var nodes = Enumerable
            .Range(1, 129)
            .Select(_ => new BulkGeneratedNode())
            .ToArray();

        var branch = new NestedSetBranch<BulkGeneratedNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<BulkGeneratedNode>(node))
                .ToArray());

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BulkGeneratedNode>()
            .ForScope(7)
            .InsertForestAsync(
                [new NestedSetTreeImport<BulkGeneratedNode, Guid>(Guid.Empty, branch)],
                CancellationToken.None));

        var restored = nodes.All(node => node is { Id: 0, Name: null, Details.Label: null }
            and { Version: 0, Details.Revision: 0 }
            and { Left: 0, Right: 0 });

        return (nodes
            .Select(node => new WeakReference<BulkGeneratedNode>(node))
            .ToArray(), error, restored);
    }
}
