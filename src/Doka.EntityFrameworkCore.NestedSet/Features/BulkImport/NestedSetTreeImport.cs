namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Associates one detached root branch with the stable identity of the tree to create.</summary>
/// <typeparam name="TEntity">The configured hierarchy entity.</typeparam>
/// <typeparam name="TTreeId">The mapped TreeId type.</typeparam>
public sealed class NestedSetTreeImport<TEntity, TTreeId>
    where TEntity : class
    where TTreeId : notnull
{
    /// <summary>Initializes one tree import.</summary>
    /// <param name="treeId">The never-before-used identity reserved for the imported tree.</param>
    /// <param name="root">The detached root branch that becomes the tree's only root.</param>
    public NestedSetTreeImport(
        TTreeId treeId,
        NestedSetBranch<TEntity> root
    )
    {
        ArgumentNullException.ThrowIfNull(treeId);
        ArgumentNullException.ThrowIfNull(root);

        TreeId = treeId;
        Root = root;
    }

    /// <summary>Gets the never-before-used identity reserved for the imported tree.</summary>
    public TTreeId TreeId { get; }

    /// <summary>Gets the detached root branch that becomes the tree's only root.</summary>
    public NestedSetBranch<TEntity> Root { get; }
}
