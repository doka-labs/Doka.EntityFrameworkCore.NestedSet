namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies transaction composition, compact structural reads and bounded repair writes.</summary>
public abstract partial class EnterpriseTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Creates a case using an independently isolated fixture-owned database.</summary>
    /// <param name="fixture">The fixture owning one lazily started database per engine.</param>
    protected EnterpriseTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Captures all mutable structure without including application payload in comparisons.</summary>
    private static Task<NodeState[]> SnapshotAsync(
        TreeContext context
    ) => context
        .Set<TreeNode>()
        .AsNoTracking()
        .OrderBy(node => node.NodeId)
        .Select(node => new NodeState(
            node.NodeId,
            node.Start,
            node.End,
            node.Depth,
            node.Position,
            node.Parent,
            node.Tree))
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Captures the canonical structure of a fixture independently of the implementation under test.</summary>
    private static NodeState[] Snapshot(
        TreeNode[] nodes
    ) => nodes
        .Select(node => new NodeState(
            node.NodeId,
            node.Start,
            node.End,
            node.Depth,
            node.Position,
            node.Parent,
            node.Tree))
        .ToArray();

    /// <summary>Starts a caller transaction with the provider's supported hierarchy isolation level.</summary>
    private static Task<IDbContextTransaction> BeginCallerAsync(
        TreeContext context
    ) => context.Database.BeginTransactionAsync(
        context.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
        CancellationToken.None);

    /// <summary>Stores structure by value so rollback assertions compare every persisted coordinate and link.</summary>
    private sealed record NodeState(
        int Id,
        long Left,
        long Right,
        int Depth,
        long Position,
        int? Parent,
        int Scope
    );
}
