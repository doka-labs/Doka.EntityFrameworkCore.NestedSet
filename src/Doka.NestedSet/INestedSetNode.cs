namespace Doka.NestedSet;

/// <summary>Provides the identity, tree identity, bounds, depth, and sibling position of an unscoped node.</summary>
/// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <remarks>
/// Implement this convenience contract only for a hierarchy without a configured scope. Scoped hierarchies use
/// <see cref="IScopedNestedSetNode{TNodeKey,TTreeId,TScope}" /> so in-memory ancestry predicates compare the complete
/// tree identity. EF Core mappings can use arbitrary entity types without implementing either interface.
/// </remarks>
public interface INestedSetNode<out TNodeKey, out TTreeId>
    where TNodeKey : notnull
    where TTreeId : notnull
{
    /// <summary>Gets the configured scalar node identity.</summary>
    /// <value>The key that identifies this node.</value>
    TNodeKey Id { get; }

    /// <summary>Gets the stable identity of the containing tree.</summary>
    /// <value>The tree identity compared before bounds are evaluated.</value>
    TTreeId TreeId { get; }

    /// <summary>Gets the inclusive left boundary.</summary>
    /// <value>The positive left boundary within this node's tree.</value>
    long Left { get; }

    /// <summary>Gets the inclusive right boundary.</summary>
    /// <value>The boundary greater than <see cref="Left" /> within this node's tree.</value>
    long Right { get; }

    /// <summary>Gets the number of ancestors; roots have depth zero.</summary>
    /// <value>The non-negative level of this node within its tree.</value>
    int Depth { get; }

    /// <summary>Gets the zero-based position among siblings in the same tree.</summary>
    /// <value>The non-negative index within this node's sibling group.</value>
    /// <remarks>The tree's single root has position zero. Non-root positions are dense for each parent.</remarks>
    long Position { get; }
}

/// <summary>Provides the complete tree identity and coordinates of a node in a scoped hierarchy.</summary>
/// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The configured scope type.</typeparam>
/// <remarks>
/// A scoped tree is identified by the pair of <see cref="Scope" /> and
/// <see cref="INestedSetNode{TNodeKey,TTreeId}.TreeId" />. Use this contract when the EF mapping calls
/// <c>HasScope</c>.
/// </remarks>
public interface IScopedNestedSetNode<out TNodeKey, out TTreeId, out TScope> : INestedSetNode<TNodeKey, TTreeId>
    where TNodeKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Gets the partition that contains the tree.</summary>
    /// <value>The scope compared together with the tree identity before bounds are evaluated.</value>
    TScope Scope { get; }
}
