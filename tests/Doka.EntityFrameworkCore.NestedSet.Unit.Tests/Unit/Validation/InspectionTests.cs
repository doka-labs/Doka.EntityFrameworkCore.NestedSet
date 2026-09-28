namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies pure inspection with large, adversarially wide and deep structural snapshots.</summary>
[Trait("Category", "Inspection")]
public sealed class InspectionTests
{
    /// <summary>The shared input size for scalability regression cases, independent of database speed.</summary>
    private const int LargeNodeCount = 1_000_000;

    /// <summary>The ordered diagnostics for corrupted intervals and depths with otherwise valid adjacency.</summary>
    private static readonly string[] s_corruptCoordinateErrors =
    [
        "Intervals do not match the ordered adjacency tree.", "Depths do not match the ordered adjacency tree.",
    ];

    /// <summary>Verifies that a valid million-node forest produces neither diagnostics nor repair rows.</summary>
    /// <param name="deep">Whether the forest is a single chain rather than a root with leaves.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MillionNodeForestNeedsNoRepairs(
        bool deep
    )
    {
        // Arrange
        var (nodes, parents) = CreateForest(LargeNodeCount, deep);

        // Act
        var result = NestedSetInspector<int>.Inspect(
            nodes,
            parents,
            EqualityComparer<int>.Default,
            true,
            CancellationToken.None);

        // Assert
        Assert.True(result.CanRebuild);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Repairs);
    }

    /// <summary>Verifies that a million corrupt rows produce errors without retaining a repair plan.</summary>
    /// <param name="deep">Whether the forest is a single chain rather than a root with leaves.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MillionCorruptNodesDoNotRetainValidationRepairs(
        bool deep
    )
    {
        // Arrange
        var (nodes, parents) = CreateForest(LargeNodeCount, deep);
        for (var index = 0; index < nodes.Length; index++)
        {
            nodes[index] = nodes[index] with
            {
                Left = 0,
                Right = 0,
                Depth = -1,
            };
        }

        // Act
        var result = NestedSetInspector<int>.Inspect(
            nodes,
            parents,
            EqualityComparer<int>.Default,
            false,
            CancellationToken.None);

        // Assert
        Assert.True(result.CanRebuild);
        Assert.Equal(s_corruptCoordinateErrors, result.Errors);
        Assert.Empty(result.Repairs);
    }

    /// <summary>Verifies that persisted 64-bit coordinates are repaired without narrowing to 32 bits.</summary>
    [Fact]
    public void RepairPlanPreservesTheLongCoordinateContract()
    {
        // Arrange
        var (nodes, parents) = CreateForest(128, false);
        nodes[64] = nodes[64] with
        {
            Left = (long)int.MaxValue + 1,
            Right = (long)int.MaxValue + 2,
            Depth = -1,
        };

        // Act
        var result = NestedSetInspector<int>.Inspect(
            nodes,
            parents,
            EqualityComparer<int>.Default,
            true,
            CancellationToken.None);

        // Assert
        Assert.True(result.CanRebuild);
        Assert.Equal(s_corruptCoordinateErrors, result.Errors);
        Assert.Equal(new NestedSetRepair<int>(65, 128, 129, 1, 63), Assert.Single(result.Repairs));
    }

    /// <summary>Verifies cancellation before even an empty snapshot can be accepted as valid.</summary>
    [Fact]
    public void CanceledInspectionDoesNotReturnAValidationResult()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);
        var parents = new Dictionary<int, int>();

        // Act
        var exception = Record.Exception(() => NestedSetInspector<int>.Inspect(
            [],
            parents,
            EqualityComparer<int>.Default,
            false,
            cancellationToken));

        // Assert
        Assert.IsType<OperationCanceledException>(exception);
    }

    /// <summary>Counts every damaged coordinate while retaining at most the requested diagnostic sample.</summary>
    [Fact]
    public void FullInspectionBoundsIssueKeysWithoutLosingCounts()
    {
        // Arrange
        const int count = 20_000;
        var (nodes, parents) = CreateForest(count, false);
        for (var index = 0; index < nodes.Length; index++)
        {
            nodes[index] = nodes[index] with
            {
                Left = 0,
                Right = 0,
            };
        }

        // Act
        var result = NestedSetInspector<int>.Inspect(
            nodes,
            parents,
            EqualityComparer<int>.Default,
            false,
            CancellationToken.None,
            collectIssues: true);

        // Assert
        Assert.True(result.CanRebuild);
        Assert.Equal(1024, result.Issues.Count);
        Assert.Equal(count, result.TotalIssueCount);
        Assert.Equal(count, result.IssueCounts[NestedSetValidationCode.InvalidBounds]);
        Assert.Empty(result.TreeIssues);
    }

    /// <summary>An empty selected tree reports its root invariant with a typed tree-wide issue.</summary>
    [Fact]
    public void EmptyTreeHasTypedRootIssue()
    {
        // Arrange
        var parents = new Dictionary<int, int>();

        // Act
        var result = NestedSetInspector<int>.Inspect(
            [],
            parents,
            EqualityComparer<int>.Default,
            false,
            CancellationToken.None,
            collectIssues: true);

        // Assert
        Assert.False(result.CanRebuild);
        Assert.Empty(result.Issues);
        Assert.Equal(NestedSetValidationCode.InvalidRootCount, Assert.Single(result.TreeIssues).Code);
        Assert.Equal(1, result.TotalIssueCount);
    }

    /// <summary>Builds structural inputs without entities, database work, or recursive fixture traversal.</summary>
    /// <param name="count">The positive node count including the single root.</param>
    /// <param name="deep">Whether each successive node is a child of its predecessor.</param>
    /// <returns>The dense forest snapshot and canonical child-to-parent links.</returns>
    private static (NestedSetNode<int>[] Nodes, Dictionary<int, int> Parents) CreateForest(
        int count,
        bool deep
    )
    {
        var nodes = new NestedSetNode<int>[count];
        var parents = new Dictionary<int, int>(count - 1);
        nodes[0] = new NestedSetNode<int>(1, 1, count * 2, default, 0, 0);

        var root = new NestedSetParent<int>(true, 1);

        for (var index = 1; index < count; index++)
        {
            var parent = deep ? index : 1;
            var optionalParent = new NestedSetParent<int>(true, parent);
            nodes[index] = deep
                ? new NestedSetNode<int>(index + 1, index + 1, (count * 2) - index, optionalParent, index, 0)
                : new NestedSetNode<int>(index + 1, index * 2, (index * 2) + 1, root, 1, index - 1);

            parents.Add(index + 1, parent);
        }

        return (nodes, parents);
    }
}
