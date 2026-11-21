namespace Doka.NestedSet;

/// <summary>Provides allocation-free predicates for nodes whose complete tree identity is available.</summary>
[PublicAPI]
public static class NestedSetNodeExtensions
{
    /// <summary>
    /// Compares bounds within one unscoped tree.
    /// </summary>
    /// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
    /// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
    /// <param name="node">
    /// The node whose hierarchy relationships are compared.
    /// </param>
    extension<TNodeKey, TTreeId>(INestedSetNode<TNodeKey, TTreeId> node)
        where TNodeKey : notnull
        where TTreeId : notnull
    {
        /// <summary>Tests whether an unscoped node is a strict ancestor of another node in the same tree.</summary>
        /// <param name="other">The candidate descendant.</param>
        /// <returns>
        /// <see langword="true" /> when both nodes have the same tree identity and the first bounds strictly contain
        /// the second bounds; otherwise, <see langword="false" />.
        /// </returns>
        /// <remarks>
        /// Tree identities are compared with <see cref="EqualityComparer{T}.Default" />. Use the scoped overload for an
        /// EF mapping configured with <c>HasScope</c>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Either node is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Either node has nonpositive or unordered bounds.</exception>
        /// <exception cref="ArgumentException">Either node has an odd inclusive width.</exception>
        public bool IsAncestorOf(
            INestedSetNode<TNodeKey, TTreeId> other
        )
        {
            var (nodeBounds, otherBounds) = ValidateBounds(node, other);

            return EqualityComparer<TTreeId>.Default.Equals(node.TreeId, other.TreeId)
                && nodeBounds.Contains(otherBounds);
        }

        /// <summary>Tests whether an unscoped node is a strict descendant of another node in the same tree.</summary>
        /// <param name="other">The candidate ancestor.</param>
        /// <returns>
        /// <see langword="true" /> when both nodes have the same tree identity and the other bounds strictly contain
        /// the first bounds; otherwise, <see langword="false" />.
        /// </returns>
        /// <exception cref="ArgumentNullException">Either node is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Either node has nonpositive or unordered bounds.</exception>
        /// <exception cref="ArgumentException">Either node has an odd inclusive width.</exception>
        public bool IsDescendantOf(
            INestedSetNode<TNodeKey, TTreeId> other
        ) => other.IsAncestorOf(node);
    }

    /// <summary>
    /// Compares bounds within one complete scoped tree identity.
    /// </summary>
    /// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
    /// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
    /// <typeparam name="TScope">The configured scope type.</typeparam>
    /// <param name="node">
    /// The node whose hierarchy relationships are compared.
    /// </param>
    extension<TNodeKey, TTreeId, TScope>(IScopedNestedSetNode<TNodeKey, TTreeId, TScope> node)
        where TNodeKey : notnull
        where TTreeId : notnull
        where TScope : notnull
    {
        /// <summary>Tests whether a scoped node is a strict ancestor of another node in the same scoped tree.</summary>
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
        public bool IsAncestorOf(
            IScopedNestedSetNode<TNodeKey, TTreeId, TScope> other
        )
        {
            var (nodeBounds, otherBounds) = ValidateBounds(node, other);

            return EqualityComparer<TScope>.Default.Equals(node.Scope, other.Scope)
                && EqualityComparer<TTreeId>.Default.Equals(node.TreeId, other.TreeId)
                && nodeBounds.Contains(otherBounds);
        }

        /// <summary>
        /// Tests whether a scoped node is a strict descendant of another node in the same scoped tree.
        /// </summary>
        /// <param name="other">The candidate ancestor.</param>
        /// <returns>
        /// <see langword="true" /> when both nodes have equal scope and tree identities and the other bounds strictly
        /// contain the first bounds; otherwise, <see langword="false" />.
        /// </returns>
        /// <exception cref="ArgumentNullException">Either node is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Either node has nonpositive or unordered bounds.</exception>
        /// <exception cref="ArgumentException">Either node has an odd inclusive width.</exception>
        public bool IsDescendantOf(
            IScopedNestedSetNode<TNodeKey, TTreeId, TScope> other
        ) => other.IsAncestorOf(node);
    }

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
