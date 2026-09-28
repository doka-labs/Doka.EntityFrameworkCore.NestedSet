namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Bridges runtime EF entity metadata to typed, provider-translatable save coordination.</summary>
internal abstract class NestedSetSaveGroup
{
    private static readonly ConditionalWeakTable<IModel, FactoryCache> s_models = new();

    /// <summary>Creates one coordinator for each affected entity type using model-lifetime factories.</summary>
    /// <param name="context">The context whose entries and metadata define this save.</param>
    /// <param name="entries">The modified hierarchy entries discovered before persistence.</param>
    /// <returns>Typed groups without retaining the context in the factory cache.</returns>
    internal static IReadOnlyList<NestedSetSaveGroup> Create(
        DbContext context,
        IReadOnlyList<EntityEntry> entries
    )
    {
        var factories = s_models.GetValue(context.Model, static _ => new FactoryCache());
        var groups = new List<NestedSetSaveGroup>();

        foreach (var group in entries.GroupBy(entry => entry.Metadata))
        {
            Func<DbContext, EntityEntry[], NestedSetSaveGroup> factory;

            lock (factories.Factories)
            {
                if (!factories.Factories.TryGetValue(group.Key, out factory!))
                {
                    var entity = group.Key;
                    var descriptor = NestedSetModelMapping
                        .For(context.Model)
                        .Descriptor(entity);

                    var scopeType = descriptor.Scope?.ClrType ?? typeof(NestedSetNoScope);

                    // WHY: Close generic queries once per EF model; no context or tracked entity enters the cache.
                    factory = typeof(NestedSetSaveGroup).GetMethod(
                            nameof(CreateTyped),
                            BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(
                            entity.ClrType,
                            descriptor.NodeKey.ClrType,
                            descriptor.TreeId.ClrType,
                            scopeType)
                        .CreateDelegate<Func<DbContext, EntityEntry[], NestedSetSaveGroup>>();

                    factories.Factories.Add(entity, factory);
                }
            }

            groups.Add(factory(context, group.ToArray()));
        }

        return groups;
    }

    /// <summary>Resolves immutable persisted scope values before constructing the global write-lock plan.</summary>
    /// <param name="requests">The combined lock requests across all affected entity types.</param>
    /// <param name="cancellationToken">The token for bounded key and scope queries.</param>
    internal abstract Task ResolveScopesAsync(
        List<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    );

    /// <summary>Checks locked tree identities and prepares Parent moves before payload persistence.</summary>
    internal abstract Task PrepareLockedChangesAsync(
        CancellationToken cancellationToken
    );

    /// <summary>Temporarily removes Parent writes from the one underlying Entity Framework Core save.</summary>
    internal abstract void SuppressParentChanges();

    /// <summary>Restores requested Parent values after the underlying save completes.</summary>
    internal abstract void RestoreParentChanges();

    /// <summary>Applies requested Parent changes through the exact-tree move engine.</summary>
    /// <param name="cancellationToken">The token used for locked structural reads and writes.</param>
    internal abstract Task ApplyParentChangesAsync(
        CancellationToken cancellationToken
    );

    /// <summary>Repairs saved ordering values after every affected write lock has been acquired.</summary>
    /// <param name="cancellationToken">The token for provider-native ordering and structural updates.</param>
    internal abstract Task ReorderAsync(
        CancellationToken cancellationToken
    );

    /// <summary>Refreshes tracked structure and generated concurrency tokens without payload reloads.</summary>
    /// <param name="cancellationToken">The token for bounded tracked-identity projections.</param>
    internal abstract Task RefreshAsync(
        CancellationToken cancellationToken
    );

    /// <summary>Creates the closed generic coordinator invoked by the cached metadata factory.</summary>
    private static NestedSetSaveGroup<TEntity, TKey, TTreeId, TScope> CreateTyped<TEntity, TKey, TTreeId, TScope>(
        DbContext context,
        EntityEntry[] entries
    )
        where TEntity : class
        where TKey : notnull
        where TTreeId : notnull
        where TScope : notnull => new(context, entries);

    /// <summary>Keeps factory lifetime bounded by the EF model while permitting concurrent context creation.</summary>
    private sealed class FactoryCache
    {
        /// <summary>Stores closed generic factories guarded by this dictionary's monitor.</summary>
        internal Dictionary<IEntityType, Func<DbContext, EntityEntry[], NestedSetSaveGroup>> Factories { get; } = new();
    }
}
