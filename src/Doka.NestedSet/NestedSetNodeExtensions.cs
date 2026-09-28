namespace Doka.NestedSet;

/// <summary>Provides allocation-free predicates for nodes whose complete tree identity is available.</summary>
public static class NestedSetNodeExtensions
{
    /// <summary>Tests whether an unscoped node is a strict ancestor of another node in the same tree.</summary>
    /// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
    /// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
    /// <param name="node">The candidate ancestor.</param>
    /// <param name="other">The candidate descendant.</param>
    /// <returns>
    /// <see langword="true" /> when both nodes have the same tree identity and the first bounds strictly contain the
    /// second bounds; otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    /// Tree identities are compared with <see cref="EqualityComparer{T}.Default" />. Use the scoped overload for an
    /// EF mapping configured with <c>HasScope</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either node is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Either node has nonpositive or unordered bounds.</exception>
    /// <exception cref="ArgumentException">Either node has an odd inclusive width.</exception>
    public static bool IsAncestorOf<TNodeKey, TTreeId>(
        this INestedSetNode<TNodeKey, TTreeId> node,
        INestedSetNode<TNodeKey, TTreeId> other
    )
        where TNodeKey : notnull
        where TTreeId : notnull
    {
        var (nodeBounds, otherBounds) = ValidateBounds(node, other);

        return EqualityComparer<TTreeId>.Default.Equals(node.TreeId, other.TreeId) && nodeBounds.Contains(otherBounds);
    }

    /// <summary>Tests whether a scoped node is a strict ancestor of another node in the same scoped tree.</summary>
    /// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
    /// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
    /// <typeparam name="TScope">The configured scope type.</typeparam>
    /// <param name="node">The candidate ancestor.</param>
    /// <param name="other">The candidate descendant.</param>
    /// <returns>
    /// <see langword="true" /> when both nodes have equal scope and tree identities and the first bounds strictly
    /// contain the second bounds; otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    /// Scope and tree identities are compared with their respective <see cref="EqualityComparer{T}.Default" />
    /// instances.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either node is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Either node has nonpositive or unordered bounds.</exception>
    /// <exception cref="ArgumentException">Either node has an odd inclusive width.</exception>
    public static bool IsAncestorOf<TNodeKey, TTreeId, TScope>(
        this IScopedNestedSetNode<TNodeKey, TTreeId, TScope> node,
        IScopedNestedSetNode<TNodeKey, TTreeId, TScope> other
    )
        where TNodeKey : notnull
        where TTreeId : notnull
        where TScope : notnull
    {
        var (nodeBounds, otherBounds) = ValidateBounds(node, other);

        return EqualityComparer<TScope>.Default.Equals(node.Scope, other.Scope)
            && EqualityComparer<TTreeId>.Default.Equals(node.TreeId, other.TreeId)
            && nodeBounds.Contains(otherBounds);
    }

    /// <summary>Tests whether an unscoped node is a strict descendant of another node in the same tree.</summary>
    /// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
    /// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
    /// <param name="node">The candidate descendant.</param>
    /// <param name="other">The candidate ancestor.</param>
    /// <returns>
    /// <see langword="true" /> when both nodes have the same tree identity and the other bounds strictly contain the
    /// first bounds; otherwise, <see langword="false" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either node is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Either node has nonpositive or unordered bounds.</exception>
    /// <exception cref="ArgumentException">Either node has an odd inclusive width.</exception>
    public static bool IsDescendantOf<TNodeKey, TTreeId>(
        this INestedSetNode<TNodeKey, TTreeId> node,
        INestedSetNode<TNodeKey, TTreeId> other
    )
        where TNodeKey : notnull
        where TTreeId : notnull => other.IsAncestorOf(node);

    /// <summary>Tests whether a scoped node is a strict descendant of another node in the same scoped tree.</summary>
    /// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
    /// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
    /// <typeparam name="TScope">The configured scope type.</typeparam>
    /// <param name="node">The candidate descendant.</param>
    /// <param name="other">The candidate ancestor.</param>
    /// <returns>
    /// <see langword="true" /> when both nodes have equal scope and tree identities and the other bounds strictly
    /// contain the first bounds; otherwise, <see langword="false" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either node is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Either node has nonpositive or unordered bounds.</exception>
    /// <exception cref="ArgumentException">Either node has an odd inclusive width.</exception>
    public static bool IsDescendantOf<TNodeKey, TTreeId, TScope>(
        this IScopedNestedSetNode<TNodeKey, TTreeId, TScope> node,
        IScopedNestedSetNode<TNodeKey, TTreeId, TScope> other
    )
        where TNodeKey : notnull
        where TTreeId : notnull
        where TScope : notnull => other.IsAncestorOf(node);

    private static (NestedSetBounds Node, NestedSetBounds Other) ValidateBounds<TNodeKey, TTreeId>(
        INestedSetNode<TNodeKey, TTreeId> node,
        INestedSetNode<TNodeKey, TTreeId> other
    )
        where TNodeKey : notnull
        where TTreeId : notnull
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(other);

        var nodeBounds = new NestedSetBounds(node.Left, node.Right);
        var otherBounds = new NestedSetBounds(other.Left, other.Right);

        return (nodeBounds, otherBounds);
    }
}
