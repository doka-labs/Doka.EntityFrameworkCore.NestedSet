namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies each nested-set operation independently against the supported relational engines.</summary>
/// <remarks>
///     Cases share a class-owned database, but each case starts with empty tables and lock rows.
/// </remarks>
public abstract partial class RelationalTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Creates a case using the shared, sequentially accessed database fixture.</summary>
    /// <param name="fixture">The fixture that owns one database per requested engine.</param>
    protected RelationalTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Creates an untracked node whose structural values will be assigned by the service.</summary>
    private static TreeNode Node(
        int id
    ) => new() { NodeId = id };

    /// <summary>Captures every structural value in key order for rollback and scope-isolation comparisons.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<string[]> SnapshotAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken
    ) => (await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == scope)
            .OrderBy(x => x.NodeId)
            .ToListAsync(cancellationToken))
        .Select(x => $"{x.NodeId}:{x.Start}:{x.End}:{x.Parent}:{x.Depth}:{x.Position}:{x.Tree}:{x.TreeId}")
        .ToArray();
}
