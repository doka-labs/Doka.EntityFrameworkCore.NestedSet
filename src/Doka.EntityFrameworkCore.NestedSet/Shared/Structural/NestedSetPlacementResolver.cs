namespace Doka.EntityFrameworkCore.NestedSet.Shared.Structural;

/// <summary>Resolves insertion and move destinations under the same operation lock.</summary>
/// <typeparam name="TEntity">The mapped domain entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The scope type.</typeparam>
internal sealed class NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;

    /// <summary>Uses the same exact-tree store as the calling algorithm.</summary>
    /// <param name="store">The shared tree queries used while the write lock is held.</param>
    internal NestedSetPlacementResolver(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store
    )
    {
        _store = store;
    }

    /// <summary>Resolves the insertion coordinates of a root, child, or sibling under the write lock.</summary>
    /// <param name="anchor">The existing parent or sibling key; ignored for a new root.</param>
    /// <param name="placement">The placement relative to the anchor, or null to create the tree's only root.</param>
    /// <param name="maximum">The tree's current maximum right boundary; zero for a new tree.</param>
    /// <param name="cancellationToken">The token used for anchor and sibling reads.</param>
    /// <param name="knownAnchor">An anchor already read and checked under this operation's lock.</param>
    /// <returns>Coordinates before any source interval is removed or destination gap is opened.</returns>
    /// <exception cref="InvalidOperationException">The anchor is missing or its coordinates are invalid.</exception>
    /// <exception cref="OverflowException">The destination exceeds the supported 64-bit coordinates.</exception>
    internal async Task<Destination> ResolveAsync(
        TKey? anchor,
        NestedSetPlacement? placement,
        long maximum,
        CancellationToken cancellationToken,
        NestedSetNode<TKey>? knownAnchor = null
    )
    {
        if (placement is null)
        {
            if (maximum != 0)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.OperationRejected,
                    "A tree can contain only one root. Create another tree with a distinct TreeId.");
            }

            return new Destination(1, default, 0, 0, Append: true);
        }

        // WHY: Moves already read the destination to reject cycles; reuse that locked structural snapshot.
        var target = knownAnchor
            ?? await _store
                .FindAsync(anchor!, cancellationToken)
                .ConfigureAwait(false);

        NestedSetGuards.RequireBounds(target);

        if (placement is NestedSetPlacement.Before or NestedSetPlacement.After
            && !target.Parent.HasValue)
        {
            // WHY: A root has no sibling group inside its tree. Treating it as an ordinary anchor would create
            // a second root and share one registry identity between two independent hierarchies.
            throw new NestedSetException(
                NestedSetErrorCode.OperationRejected,
                "A tree root cannot have siblings. Use a child placement or a distinct TreeId.");
        }

        return placement switch
        {
            NestedSetPlacement.FirstChild => new Destination(
                checked(target.Left + 1),
                new NestedSetParent<TKey>(true, target.Key),
                checked(target.Depth + 1),
                0),
            NestedSetPlacement.LastChild => new Destination(
                target.Right,
                new NestedSetParent<TKey>(true, target.Key),
                checked(target.Depth + 1),
                await _store
                    .Siblings(new NestedSetParent<TKey>(true, target.Key))
                    .CountAsync(cancellationToken)
                    .ConfigureAwait(false),
                Append: true),
            NestedSetPlacement.Before => new Destination(target.Left, target.Parent, target.Depth, target.Position),
            NestedSetPlacement.After => new Destination(
                checked(target.Right + 1),
                target.Parent,
                target.Depth,
                checked(target.Position + 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };
    }

    /// <summary>Describes a node's destination before a mutation changes the tree's coordinates.</summary>
    /// <param name="Boundary">The left boundary at which to open the destination interval.</param>
    /// <param name="Parent">The typed destination parent, or an absent value for a root.</param>
    /// <param name="Depth">The destination depth, starting at zero for roots.</param>
    /// <param name="Position">The zero-based destination sibling position.</param>
    /// <param name="Append">Whether the destination follows all existing siblings, making a target shift empty.</param>
    internal readonly record struct Destination(
        long Boundary,
        NestedSetParent<TKey> Parent,
        int Depth,
        long Position,
        bool Append = false
    );
}
