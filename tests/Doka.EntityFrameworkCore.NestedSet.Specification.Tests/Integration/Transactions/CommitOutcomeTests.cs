namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that a failed commit acknowledgment cannot be interpreted as a rolled-back mutation.</summary>
public abstract class CommitOutcomeTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Creates cases against independently reset real relational databases.</summary>
    /// <param name="fixture">The fixture owning the isolated provider database.</param>
    protected CommitOutcomeTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>An exception after commit preserves its cause while the committed node remains visible once.</summary>
    [Fact]
    public async Task PostCommitFailureRequiresFreshContextReconciliation()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new PostCommitFailureProbe();
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var node = new TreeNode
        {
            NodeId = 41,
            Payload = "Committed once",
        };

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(node, Guid.Empty, CancellationToken.None));

        // Assert
        // WHY: Rollback after a successful commit can also fail; the contract preserves both original causes.
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
        await using var verification = database.CreateContext();
        var persisted = await verification
            .Set<TreeNode>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);
        Assert.Equal(
            (41, 1, 2, 0, 0),
            (persisted.NodeId, persisted.Start, persisted.End, persisted.Depth, persisted.Position));
        Assert.Equal("Committed once", persisted.Payload);
        var verifiedTree = verification
            .NestedSet<TreeNode>()
            .ForScope(1);
        Assert.Empty(
            (await verifiedTree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }
}
