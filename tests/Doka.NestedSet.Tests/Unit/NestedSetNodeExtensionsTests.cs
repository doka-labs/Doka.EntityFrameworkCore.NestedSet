namespace Doka.NestedSet.Tests;

/// <summary>Verifies strict hierarchy predicates and their invalid-input contracts.</summary>
public sealed class NestedSetNodeExtensionsTests
{
    /// <summary>Verifies ancestor relationships without treating equality or siblings as ancestry.</summary>
    /// <param name="left">The candidate ancestor's left boundary.</param>
    /// <param name="right">The candidate ancestor's right boundary.</param>
    /// <param name="otherLeft">The other node's left boundary.</param>
    /// <param name="otherRight">The other node's right boundary.</param>
    /// <param name="expected">Whether the first node is a strict ancestor.</param>
    [Theory]
    [InlineData(1L, 8L, 2L, 5L, true)]
    [InlineData(2L, 5L, 1L, 8L, false)]
    [InlineData(1L, 8L, 1L, 8L, false)]
    [InlineData(2L, 5L, 6L, 7L, false)]
    [InlineData(2L, long.MaxValue, long.MaxValue - 2, long.MaxValue - 1, true)]
    public void AncestorRequiresStrictContainment(
        long left,
        long right,
        long otherLeft,
        long otherRight,
        bool expected
    )
    {
        // Arrange
        var node = new Node(1, left, right);
        var other = new Node(2, otherLeft, otherRight);

        // Act
        var actual = node.IsAncestorOf(other);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Verifies descendant relationships without treating equality or siblings as descent.</summary>
    /// <param name="left">The candidate descendant's left boundary.</param>
    /// <param name="right">The candidate descendant's right boundary.</param>
    /// <param name="otherLeft">The other node's left boundary.</param>
    /// <param name="otherRight">The other node's right boundary.</param>
    /// <param name="expected">Whether the first node is a strict descendant.</param>
    [Theory]
    [InlineData(2L, 5L, 1L, 8L, true)]
    [InlineData(1L, 8L, 2L, 5L, false)]
    [InlineData(1L, 8L, 1L, 8L, false)]
    [InlineData(2L, 5L, 6L, 7L, false)]
    [InlineData(long.MaxValue - 2, long.MaxValue - 1, 2L, long.MaxValue, true)]
    public void DescendantRequiresStrictContainment(
        long left,
        long right,
        long otherLeft,
        long otherRight,
        bool expected
    )
    {
        // Arrange
        var node = new Node(1, left, right);
        var other = new Node(2, otherLeft, otherRight);

        // Act
        var actual = node.IsDescendantOf(other);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Verifies that matching bounds in different trees never imply ancestry.</summary>
    [Fact]
    public void AncestorRejectsMatchingBoundsFromDifferentTree()
    {
        // Arrange
        var ancestor = new Node(1, 1, 8, TreeId: Guid.NewGuid());
        var descendant = new Node(2, 2, 5, TreeId: Guid.NewGuid());

        // Act
        var actual = ancestor.IsAncestorOf(descendant);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Verifies that an unscoped hierarchy also rejects matching bounds from another tree.</summary>
    [Fact]
    public void UnscopedAncestorRejectsMatchingBoundsFromDifferentTree()
    {
        // Arrange
        var ancestor = new UnscopedNode(1, Guid.NewGuid(), 1, 8);
        var descendant = new UnscopedNode(2, Guid.NewGuid(), 2, 5);

        // Act
        var actual = ancestor.IsAncestorOf(descendant);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Verifies that an unscoped hierarchy accepts strict containment within the same tree.</summary>
    [Fact]
    public void UnscopedAncestorAcceptsSameTree()
    {
        // Arrange
        var treeId = Guid.NewGuid();
        var ancestor = new UnscopedNode(1, treeId, 1, 8);
        var descendant = new UnscopedNode(2, treeId, 2, 5);

        // Act
        var actual = ancestor.IsAncestorOf(descendant);

        // Assert
        Assert.True(actual);
    }

    /// <summary>Verifies that matching tree identities and bounds in different scopes never imply ancestry.</summary>
    [Fact]
    public void AncestorRejectsMatchingBoundsFromDifferentScope()
    {
        // Arrange
        var treeId = Guid.NewGuid();
        var ancestor = new Node(1, 1, 8, TreeId: treeId, Scope: "first");
        var descendant = new Node(2, 2, 5, TreeId: treeId, Scope: "second");

        // Act
        var actual = ancestor.IsAncestorOf(descendant);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Verifies that descendant checks reject matching bounds from another scoped tree.</summary>
    [Fact]
    public void DescendantRejectsMatchingBoundsFromDifferentTree()
    {
        // Arrange
        var descendant = new Node(2, 2, 5, TreeId: Guid.NewGuid());
        var ancestor = new Node(1, 1, 8, TreeId: Guid.NewGuid());

        // Act
        var actual = descendant.IsDescendantOf(ancestor);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Verifies that descendant checks reject matching bounds from another scope.</summary>
    [Fact]
    public void DescendantRejectsMatchingBoundsFromDifferentScope()
    {
        // Arrange
        var treeId = Guid.NewGuid();
        var descendant = new Node(2, 2, 5, TreeId: treeId, Scope: "first");
        var ancestor = new Node(1, 1, 8, TreeId: treeId, Scope: "second");

        // Act
        var actual = descendant.IsDescendantOf(ancestor);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Verifies that a scoped hierarchy accepts strict descent within the same scope and tree.</summary>
    [Fact]
    public void ScopedDescendantAcceptsSameScopeAndTree()
    {
        // Arrange
        var treeId = Guid.NewGuid();
        var descendant = new Node(2, 2, 5, TreeId: treeId, Scope: "scope");
        var ancestor = new Node(1, 1, 8, TreeId: treeId, Scope: "scope");

        // Act
        var actual = descendant.IsDescendantOf(ancestor);

        // Assert
        Assert.True(actual);
    }

    /// <summary>Verifies that ancestor checks reject another node with invalid bounds.</summary>
    [Fact]
    public void AncestorRejectsInvalidNode()
    {
        // Arrange
        var root = new Node(1, 1, 8);
        var invalid = new Node(2, 0, 0);

        // Act
        var exception = Record.Exception(() => root.IsAncestorOf(invalid));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that ancestor checks reject invalid bounds on the candidate ancestor.</summary>
    [Fact]
    public void AncestorRejectsInvalidReceiver()
    {
        // Arrange
        var invalid = new Node(1, 0, 0);
        var descendant = new Node(2, 2, 3);

        // Act
        var exception = Record.Exception(() => invalid.IsAncestorOf(descendant));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that ancestor checks reject a null candidate descendant.</summary>
    [Fact]
    public void AncestorRejectsNullDescendant()
    {
        // Arrange
        var root = new Node(1, 1, 8);

        // Act
        var exception = Record.Exception(() => root.IsAncestorOf(null!));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>Verifies that ancestor checks reject a null extension-method receiver.</summary>
    [Fact]
    public void AncestorRejectsNullReceiver()
    {
        // Arrange
        var root = new Node(1, 1, 8);

        // Act
        var exception = Record.Exception(() => NestedSetNodeExtensions.IsAncestorOf(null!, root));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>Verifies that descendant checks reject a null candidate ancestor.</summary>
    [Fact]
    public void DescendantRejectsNullAncestor()
    {
        // Arrange
        var root = new Node(1, 1, 8);

        // Act
        var exception = Record.Exception(() => root.IsDescendantOf(null!));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>Verifies that descendant checks reject invalid bounds on the candidate descendant.</summary>
    [Fact]
    public void DescendantRejectsInvalidReceiver()
    {
        // Arrange
        var invalid = new Node(2, 0, 0);
        var ancestor = new Node(1, 1, 4);

        // Act
        var exception = Record.Exception(() => invalid.IsDescendantOf(ancestor));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that node coordinates preserve values beyond 32-bit range.</summary>
    [Fact]
    public void NodeContractPreservesLongCoordinates()
    {
        // Arrange
        IScopedNestedSetNode<int, Guid, string> node = new Node(
            1,
            (long)int.MaxValue + 1,
            (long)int.MaxValue + 2,
            Position: (long)int.MaxValue + 1);

        // Act
        var coordinates = (node.Left, node.Right, node.Position);

        // Assert
        Assert.Equal(((long)int.MaxValue + 1, (long)int.MaxValue + 2, (long)int.MaxValue + 1), coordinates);
    }

    /// <summary>Supplies immutable node values without persistence for predicate tests.</summary>
    /// <param name="Id">The identity used by the interface contract.</param>
    /// <param name="Left">The inclusive left boundary.</param>
    /// <param name="Right">The inclusive right boundary.</param>
    /// <param name="Depth">The zero-based level in the tree.</param>
    /// <param name="Position">The zero-based sibling position.</param>
    /// <param name="TreeId">The stable tree identity.</param>
    /// <param name="Scope">The partition containing the tree.</param>
    private sealed record Node(
        int Id,
        long Left,
        long Right,
        int Depth = 0,
        long Position = 0,
        Guid TreeId = default,
        string Scope = "scope"
    ) : IScopedNestedSetNode<int, Guid, string>;

    /// <summary>Supplies immutable unscoped values for the tree-identity overload.</summary>
    /// <param name="Id">The configured scalar node identity.</param>
    /// <param name="TreeId">The stable tree identity.</param>
    /// <param name="Left">The inclusive left boundary.</param>
    /// <param name="Right">The inclusive right boundary.</param>
    /// <param name="Depth">The zero-based level in the tree.</param>
    /// <param name="Position">The zero-based sibling position.</param>
    private sealed record UnscopedNode(
        int Id,
        Guid TreeId,
        long Left,
        long Right,
        int Depth = 0,
        long Position = 0
    ) : INestedSetNode<int, Guid>;
}
