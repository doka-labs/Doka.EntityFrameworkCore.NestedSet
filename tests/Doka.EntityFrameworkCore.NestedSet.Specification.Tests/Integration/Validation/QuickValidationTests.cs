namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the tree-wide invariants checked without materializing hierarchy rows.</summary>
public abstract class QuickValidationTests : ProviderTest
{
    private static readonly Guid s_treeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly RelationalFixture _fixture;

    /// <summary>Creates tests backed by the reusable relational provider fixture.</summary>
    /// <param name="fixture">The fixture that isolates each provider case.</param>
    protected QuickValidationTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Valid coordinates and one root produce an empty Quick report.</summary>
    [Fact]
    public async Task ValidTreePassesQuickValidation()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup.AddRangeAsync([Node(1, null, 1, 4, 0, 0), Node(2, 1, 2, 3, 1, 0),], CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_treeId);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Quick, CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetValidationLevel.Quick, report.Level);
        Assert.Equal(2, report.NodeCount);
        Assert.True(report.IsValid);
        Assert.Empty(report.Issues);
    }

    /// <summary>An exact TreeId with no nodes has no root and is reported as invalid.</summary>
    [Fact]
    public async Task EmptyTreeReportsMissingRoot()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_treeId);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Quick, CancellationToken.None);

        // Assert
        Assert.Equal(0, report.NodeCount);
        Assert.False(report.IsValid);
        Assert.Equal(
            NestedSetValidationCode.InvalidRootCount,
            Assert.Single(report.Issues)
                .Code);
    }

    /// <summary>Full validation reports an empty tree with a typed tree-wide issue and complete counts.</summary>
    [Fact]
    public async Task EmptyTreeFullReportHasTypedRootIssue()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_treeId);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.False(report.IsValid);
        Assert.Equal(1, report.TotalIssueCount);
        Assert.Equal(1, report.IssueCounts[NestedSetValidationCode.InvalidRootCount]);
        Assert.Null(
            Assert.Single(report.Issues)
                .NodeKey);
        Assert.False(report.IssuesTruncated);
    }

    /// <summary>Full validation counts all damaged rows while retaining a bounded public key sample.</summary>
    [Fact]
    public async Task FullReportBoundsIssueKeysWithoutLosingCounts()
    {
        // Arrange
        const int damagedChildren = 1_100;
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        var nodes = new TreeNode[damagedChildren + 1];
        nodes[0] = Node(1, null, 1, 2L * nodes.Length, 0, 0);
        for (var index = 1; index < nodes.Length; index++)
        {
            var left = 10_000L + (index * 2L);
            nodes[index] = Node(index + 1, 1, left, left + 1, 1, index - 1);
        }

        await setup.AddRangeAsync(nodes, CancellationToken.None);
        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_treeId);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.False(report.IsValid);
        Assert.Equal(damagedChildren, report.TotalIssueCount);
        Assert.Equal(damagedChildren, report.IssueCounts[NestedSetValidationCode.InvalidBounds]);
        Assert.Equal(1024, report.Issues.Count);
        Assert.True(report.IssuesTruncated);
    }

    /// <summary>Two roots under one TreeId are rejected even with complete, unique boundaries.</summary>
    [Fact]
    public async Task MultipleRootsReportRootCount()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup.AddRangeAsync([Node(1, null, 1, 2, 0, 0), Node(2, null, 3, 4, 0, 1),], CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_treeId);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Quick, CancellationToken.None);

        // Assert
        Assert.Equal(2, report.NodeCount);
        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, issue => issue.Code == NestedSetValidationCode.InvalidRootCount);
        Assert.DoesNotContain(report.Issues, issue => issue.Code == NestedSetValidationCode.InvalidBounds);
    }

    /// <summary>Repeated boundaries fail the aggregate interval invariant without a full traversal.</summary>
    [Fact]
    public async Task DuplicateBoundaryReportsInvalidBounds()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup.AddRangeAsync(
            [Node(1, null, 1, 6, 0, 0), Node(2, 1, 2, 3, 1, 0), Node(3, 1, 2, 5, 1, 1),],
            CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_treeId);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Quick, CancellationToken.None);

        // Assert
        Assert.Equal(3, report.NodeCount);
        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, issue => issue.Code == NestedSetValidationCode.InvalidBounds);
        Assert.DoesNotContain(report.Issues, issue => issue.Code == NestedSetValidationCode.InvalidRootCount);
    }

    /// <summary>Quick detects a positive boundary gap that passes the database check constraints.</summary>
    [Fact]
    public async Task BoundaryGapReportsInvalidBounds()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup.AddRangeAsync([Node(1, null, 1, 5, 0, 0), Node(2, 1, 2, 3, 1, 0),], CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_treeId);

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Quick, CancellationToken.None);

        // Assert
        Assert.Equal(2, report.NodeCount);
        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, issue => issue.Code == NestedSetValidationCode.InvalidBounds);
    }

    /// <summary>Positive depth and position mismatches require Full structural inspection.</summary>
    /// <param name="role">The derived property deliberately set to a wrong positive value.</param>
    /// <param name="expectedCode">The issue reported only by Full inspection.</param>
    [Theory]
    [MemberData(nameof(FullOnlyMismatches))]
    public async Task PositiveDerivedMismatchRequiresFullValidation(
        string role,
        NestedSetValidationCode expectedCode
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup.AddRangeAsync(
            [Node(1, null, 1, 4, 0, 0), Node(2, 1, 2, 3, role == "Depth" ? 2 : 1, role == "Position" ? 2 : 0),],
            CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InTree(s_treeId);

        // Act
        var quick = await tree.ValidateAsync(NestedSetValidationLevel.Quick, CancellationToken.None);
        var full = await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.True(quick.IsValid);
        Assert.Empty(quick.Issues);
        Assert.False(full.IsValid);
        Assert.Contains(full.Issues, issue => issue.Code == expectedCode && Equals(issue.NodeKey, 2));
    }

    /// <summary>Provides one Full-only mismatch per derived structural role.</summary>
    public static IEnumerable<TheoryDataRow<string, NestedSetValidationCode>> FullOnlyMismatches()
    {
        yield return new TheoryDataRow<string, NestedSetValidationCode>("Depth", NestedSetValidationCode.InvalidDepth);
        yield return new TheoryDataRow<string, NestedSetValidationCode>(
            "Position",
            NestedSetValidationCode.InvalidPosition);
    }

    /// <summary>Creates one test-only row with complete precomputed hierarchy structure.</summary>
    private static TreeNode Node(
        int id,
        int? parent,
        long left,
        long right,
        int depth,
        long position
    ) => new()
    {
        NodeId = id,
        Parent = parent,
        TreeId = s_treeId,
        Tree = 7,
        Start = left,
        End = right,
        Depth = depth,
        Position = position,
    };
}
