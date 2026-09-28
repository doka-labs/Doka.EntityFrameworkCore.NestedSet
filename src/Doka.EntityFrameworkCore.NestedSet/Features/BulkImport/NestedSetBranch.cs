namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Describes one new subtree without requiring assigned keys or mapped navigation properties.</summary>
/// <typeparam name="TEntity">The detached hierarchy entity type.</typeparam>
/// <remarks>
///     Child order is preserved when the model has no ordering rule. Otherwise the database orders siblings using
///     the configured rule after generated keys and sort values exist. The copied topology is immutable; entity
///     payload remains caller-owned and must not be changed while an import is running.
/// </remarks>
public sealed class NestedSetBranch<TEntity>
    where TEntity : class
{
    /// <summary>Creates a subtree whose child collection is copied before it can be imported.</summary>
    /// <param name="entity">The detached node without populated non-owned navigation properties.</param>
    /// <param name="children">The direct child subtrees, or null for a leaf.</param>
    /// <exception cref="ArgumentNullException">The entity or a supplied child is null.</exception>
    public NestedSetBranch(
        TEntity entity,
        IReadOnlyList<NestedSetBranch<TEntity>>? children = null
    )
    {
        ArgumentNullException.ThrowIfNull(entity);
        Entity = entity;

        var snapshot = children?.ToArray() ?? [];

        foreach (var child in snapshot)
        {
            ArgumentNullException.ThrowIfNull(child);
        }

        Children = snapshot.Length == 0 ? Array.Empty<NestedSetBranch<TEntity>>() : Array.AsReadOnly(snapshot);
    }

    /// <summary>
    ///     Gets the detached entity whose generated key and final structural values are returned in place.
    /// </summary>
    public TEntity Entity { get; }

    /// <summary>Gets the immutable direct-child topology that determines imported parent links.</summary>
    public IReadOnlyList<NestedSetBranch<TEntity>> Children { get; }
}
