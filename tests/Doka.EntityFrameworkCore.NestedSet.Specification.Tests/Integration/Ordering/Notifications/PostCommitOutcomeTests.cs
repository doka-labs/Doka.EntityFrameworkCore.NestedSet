namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Separates restored tracked state from the persisted outcome after an ordered save commits.</summary>
public abstract class PostCommitOutcomeTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Creates cases using provider-specific ordering tables.</summary>
    /// <param name="fixture">The fixture that owns reusable ordered databases.</param>
    protected PostCommitOutcomeTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Payload and repaired order can be committed even though SaveChangesAsync reports failure.</summary>
    [Fact]
    public async Task PostCommitSaveFailureDoesNotUndoPersistedOrder()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var tree = setup
            .NestedSet<OrderingNode>()
            .ForScope(1);
        var treeId = Guid.NewGuid();
        await tree.InsertRootAsync(
            new OrderingNode
            {
                Id = 10,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Alpha",
            },
            10,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 2,
                Name = "Bravo",
            },
            10,
            CancellationToken.None);

        var probe = new PostCommitFailureProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 1, CancellationToken.None);

        renamed.Name = "Zulu";

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        // WHY: Cleanup may report the already-completed transaction as a second cause without losing the first.
        if (error is AggregateException aggregate)
        {
            Assert.Contains(
                aggregate.Flatten().InnerExceptions,
                cause => ReferenceEquals(cause, probe.Failure));
        }
        else
        {
            Assert.Same(probe.Failure, error);
        }

        Assert.Equal(1, probe.CommitNotifications);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var nodes = await verification
            .Set<OrderingNode>()
            .AsNoTracking()
            .Where(node => node.ParentId == 10)
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([2, 1], nodes.Select(node => node.Id));
        Assert.Equal(["Bravo", "Zulu"], nodes.Select(node => node.Name));
        Assert.Equal([0, 1], nodes.Select(node => node.Position));
        var verifiedTree = verification
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var report = await verifiedTree
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        Assert.True(report.IsValid);
    }
}
