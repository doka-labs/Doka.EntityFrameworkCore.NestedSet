namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the public facade against every supported relational provider.</summary>
public abstract partial class NestedSetFacadeTests : ProviderTest
{
    private static readonly Guid s_firstTree = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_secondTree = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly RelationalFixture _fixture;

    /// <summary>Creates facade tests backed by reusable provider fixtures.</summary>
    /// <param name="fixture">The fixture owning the relational databases.</param>
    protected NestedSetFacadeTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Gets every query operation and its expected preorder result.</summary>
    public static IEnumerable<TheoryDataRow<string, int[]>> QueryCases
    {
        get
        {
            (string, int[])[] operations =
            [
                ("Tree", [1, 2, 3, 4]),
                ("Subtree", [2, 3]),
                ("Children", [2, 4]),
                ("Descendants", [2, 3, 4]),
                ("Ancestors", [1, 2]),
                ("Parent", [2]),
            ];

            foreach (var (operation, expected) in operations)
            {
                yield return new TheoryDataRow<string, int[]>(operation, expected);
            }
        }
    }

    /// <summary>Creates two scoped trees with overlapping bounds and one same-id tree in another Scope.</summary>
    private static async Task SeedScopedAsync(
        TestDatabase database
    )
    {
        await using var context = database.CreateContext();
        await context.AddRangeAsync(
        [
            Node(1, 7, s_firstTree, null, 1, 8, 0, 0, "managed"),
            Node(2, 7, s_firstTree, 1, 2, 5, 1, 0, "managed"),
            Node(3, 7, s_firstTree, 2, 3, 4, 2, 0, "target"),
            Node(4, 7, s_firstTree, 1, 6, 7, 1, 1, "other"),
            Node(10, 7, s_secondTree, null, 1, 4, 0, 0, "other tree"),
            Node(11, 7, s_secondTree, 10, 2, 3, 1, 0, "other tree"),
            Node(20, 8, s_firstTree, null, 1, 2, 0, 0, "other scope"),
        ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
    }

    /// <summary>Creates two scopeless trees, including one node hidden by an application query filter.</summary>
    private static async Task SeedScopelessAsync(
        TestDatabase database
    )
    {
        await using var context = database.CreateContext();
        await context.AddRangeAsync(
        [
            Unscoped(100, s_firstTree, null, 1, 8, 0, 0, true),
            Unscoped(101, s_firstTree, 100, 2, 3, 1, 0, false),
            Unscoped(102, s_firstTree, 100, 4, 7, 1, 1, true),
            Unscoped(103, s_firstTree, 102, 5, 6, 2, 0, true),
            Unscoped(110, s_secondTree, null, 1, 2, 0, 0, true),
        ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
    }

    /// <summary>Creates one scoped node fixture.</summary>
    private static TreeNode Node(
        int id,
        int scope,
        Guid treeId,
        int? parent,
        long left,
        long right,
        int depth,
        long position,
        string payload
    ) => new()
    {
        NodeId = id,
        Tree = scope,
        TreeId = treeId,
        Parent = parent,
        Start = left,
        End = right,
        Depth = depth,
        Position = position,
        Payload = payload,
    };

    /// <summary>Creates one scopeless filtered node fixture.</summary>
    private static UnscopedQueryNode Unscoped(
        int id,
        Guid treeId,
        int? parent,
        long left,
        long right,
        int depth,
        long position,
        bool visible
    ) => new()
    {
        Id = id,
        TreeId = treeId,
        ParentId = parent,
        Left = left,
        Right = right,
        Depth = depth,
        Position = position,
        Visible = visible,
    };
}
