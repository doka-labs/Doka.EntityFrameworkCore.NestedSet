namespace Doka.NestedSet;

/// <summary>Provides the complete tree identity and coordinates of a node in a scoped hierarchy.</summary>
/// <typeparam name="TNodeKey">The configured scalar node-key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The configured scope type.</typeparam>
/// <remarks>
/// A scoped tree is identified by the pair of <see cref="Scope" /> and
/// <see cref="INestedSetNode{TNodeKey,TTreeId}.TreeId" />. Use this contract when the EF mapping calls
/// <c>HasScope</c>.
/// </remarks>
[PublicAPI]
public interface IScopedNestedSetNode<out TNodeKey, out TTreeId, out TScope> : INestedSetNode<TNodeKey, TTreeId>
    where TNodeKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Gets the partition that contains the tree.</summary>
    /// <value>The scope compared together with the tree identity before bounds are evaluated.</value>
    TScope Scope { get; }
}
