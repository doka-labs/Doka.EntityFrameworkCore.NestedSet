namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies structured diagnostics identify the offending node independently of message wording.</summary>
public sealed class ValidationIssueTests
{
    /// <summary>Verifies every violation category preserves a concrete node identity.</summary>
    /// <param name="code">The corruption applied to the second node.</param>
    [Theory]
    [InlineData(NestedSetValidationCode.InvalidBounds)]
    [InlineData(NestedSetValidationCode.InvalidDepth)]
    [InlineData(NestedSetValidationCode.InvalidPosition)]
    [InlineData(NestedSetValidationCode.NegativePosition)]
    [InlineData(NestedSetValidationCode.DuplicatePosition)]
    [InlineData(NestedSetValidationCode.MissingParent)]
    [InlineData(NestedSetValidationCode.CycleOrUnreachableNode)]
    public void StructuredIssueRetainsOffendingKey(
        NestedSetValidationCode code
    )
    {
        // Arrange
        NestedSetNode<int>[] nodes =
        [
            new(0, 1, 6, default, 0, 0),
            new(1, 2, 3, new NestedSetParent<int>(true, 0), 1, 0),
            new(2, 4, 5, new NestedSetParent<int>(true, 0), 1, 1),
        ];

        nodes[2] = code switch
        {
            NestedSetValidationCode.InvalidBounds => nodes[2] with { Right = 0 },
            NestedSetValidationCode.InvalidDepth => nodes[2] with { Depth = 0 },
            NestedSetValidationCode.InvalidPosition => nodes[2] with { Position = 10 },
            NestedSetValidationCode.NegativePosition => nodes[2] with { Position = -1 },
            NestedSetValidationCode.DuplicatePosition => nodes[2] with { Position = 0 },
            NestedSetValidationCode.MissingParent => nodes[2] with { Parent = new(true, 99) },
            NestedSetValidationCode.CycleOrUnreachableNode => nodes[2] with { Parent = new(true, 2) },
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };

        // Act
        var result = NestedSetInspector<int>.Inspect(
            nodes,
            null,
            EqualityComparer<int>.Default,
            false,
            CancellationToken.None,
            collectIssues: true);

        // Assert
        var issue = Assert.Single(result.Issues, issue => issue.Code == code);
        Assert.Equal(2, issue.NodeKey);
        Assert.NotEmpty(issue.Message);
        Assert.Empty(result.Repairs);
    }
}
