namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides an explicit test-only boundary for seeding precomputed hierarchy coordinates.</summary>
internal static class HierarchyFixtureSaveExtensions
{
    private static readonly ConditionalWeakTable<IEntityType, Lazy<FixtureRequestFactory>> s_requestFactories = new();

    /// <summary>Removes registry lifecycle state after every payload table in this fixture model is reset.</summary>
    /// <param name="context">The test-only context whose hierarchy payload has already been removed.</param>
    /// <param name="cancellationToken">The token used for the registry cleanup commands.</param>
    /// <returns>A task that completes when each model-specific registry is empty.</returns>
    internal static async Task ClearNestedSetTreeRegistriesAsync(
        this DbContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        var sql = context.GetService<ISqlGenerationHelper>();

        foreach (var registry in context
                     .Model
                     .GetEntityTypes()
                     .Where(entity => entity.ClrType == typeof(NestedSetTreeRegistry)))
        {
            // WHY: An isolated full fixture reset must clear both active reservations and tombstones from that
            // context's physical model. Ordinary tree deletion retains tombstones and never uses this helper.
            var table = sql.DelimitIdentifier(registry.GetTableName()!, registry.GetSchema());
            var deleteRegistry = "DELETE FROM " + table;

            // WHY: The only interpolated identifier comes from EF metadata and is provider-delimited above.
            await context.Database.ExecuteSqlRawAsync(deleteRegistry, cancellationToken);
        }
    }

    /// <summary>Persists fixture rows whose complete hierarchy structure was calculated by the test.</summary>
    /// <param name="context">The test context containing only fixture-owned hierarchy rows.</param>
    /// <param name="cancellationToken">The token used for the fixture save.</param>
    /// <returns>The number of state entries written by Entity Framework Core.</returns>
    internal static async Task<int> SavePrecomputedHierarchyAsync(
        this DbContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        var requests = CreateRegistryRequests(context);
        var planned = context
            .ChangeTracker
            .Entries()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();

        var saved = 0;

        // WHY: Fixture seeding must share one transaction with explicit registry lifecycle transitions, so a
        // tombstone or duplicate reservation cannot leave precomputed rows committed without a valid registry.
        await NestedSetTransaction.ExecuteAsync(
            context,
            async token =>
            {
                foreach (var identity in requests)
                {
                    var registry = identity.Mapping.Registry;
                    var exists = await context
                        .Set<NestedSetTreeRegistry>(registry.Name)
                        .AsNoTracking()
                        .AnyAsync(RegistryIdentity(identity), token);

                    var mode = exists ? NestedSetTreeLockMode.Existing : NestedSetTreeLockMode.New;

                    var request = RequestFactory(identity.Mapping.Hierarchy)
                        .WithMode(identity, mode);

                    // WHY: Database collation can equate distinct CLR Scope spellings. Resolving each request after
                    // the preceding reservation makes aliases existing identities rather than duplicate new trees.
                    await NestedSetTreeLocks.AcquireAsync(context, [request], token);
                }

                // WHY: Fixtures deliberately load precomputed or corrupt coordinates. The exact insertion allowlist
                // still rejects callback-created work before the test data reaches the database.
                using var managedSave = NestedSetSaveChanges.EnterManagedSave(context, planned, static () => { });
                saved = await context.SaveChangesAsync(token);
            },
            cancellationToken);

        return saved;
    }

    /// <summary>Builds a native typed registry predicate from the snapshotted complete fixture identity.</summary>
    private static System.Linq.Expressions.Expression<Func<NestedSetTreeRegistry, bool>> RegistryIdentity(
        INestedSetTreeLockRequest request
    )
    {
        var row = System.Linq.Expressions.Expression.Parameter(typeof(NestedSetTreeRegistry), "registry");
        var tree = RegistryPropertyEquals(row, request.Mapping.TreeId, request.TreeIdValue);
        var predicate = request.Mapping.Scope is { } scope
            ? System.Linq.Expressions.Expression.AndAlso(tree, RegistryPropertyEquals(row, scope, request.ScopeValue!))
            : tree;

        return System.Linq.Expressions.Expression.Lambda<Func<NestedSetTreeRegistry, bool>>(predicate, row);
    }

    /// <summary>
    /// Retains the mapped CLR type so provider converters and native identity equality remain active.
    /// </summary>
    private static System.Linq.Expressions.BinaryExpression RegistryPropertyEquals(
        System.Linq.Expressions.ParameterExpression row,
        IProperty property,
        object value
    )
    {
        var access = System.Linq.Expressions.Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [property.ClrType],
            row,
            System.Linq.Expressions.Expression.Constant(property.Name));

        // WHY: Domain equality can equate values whose converted database identities differ. Parameterizing
        // each typed value prevents a cached constant from selecting the preceding identity's registry row.
        var parameter = System.Linq.Expressions.Expression.Call(
            typeof(EF),
            nameof(EF.Parameter),
            [property.ClrType],
            System.Linq.Expressions.Expression.Constant(value, property.ClrType));

        return System.Linq.Expressions.Expression.Equal(access, parameter);
    }

    /// <summary>Builds distinct snapshotted fixture identities using exact provider identity representations.</summary>
    private static INestedSetTreeLockRequest[] CreateRegistryRequests(
        DbContext context
    )
    {
        var modelMapping = NestedSetModelMapping.For(context.Model);
        var hierarchyEntries = context
            .ChangeTracker
            .Entries()
            .Where(entry => entry.State == EntityState.Added && modelMapping.TryDescriptor(entry.Metadata, out _))
            .ToArray();

        var requests = new HashSet<INestedSetTreeLockRequest>(RegistryRequestComparer.Instance);

        foreach (var hierarchyEntry in hierarchyEntries)
        {
            requests.Add(
                RequestFactory(hierarchyEntry.Metadata)
                    .Create(hierarchyEntry, NestedSetTreeLockMode.Existing));
        }

        return requests.ToArray();
    }

    /// <summary>Retains one context-free typed factory for each exact hierarchy mapping.</summary>
    private static FixtureRequestFactory RequestFactory(
            IEntityType entityType
        )
        // WHY: A fixture may seed different mapped role types, including named shared entities. Close the
        // factory once for model metadata; never repeat generic reflection for each payload row or context.
        => s_requestFactories.GetValue(
                entityType,
                static metadata => new Lazy<FixtureRequestFactory>(
                    () => FixtureRequestFactory.Close(metadata),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    /// <summary>Adapts heterogeneous fixture metadata to requests retaining their exact mapped role types.</summary>
    private abstract class FixtureRequestFactory
    {
        /// <summary>Closes the factory over the metadata's TreeId and optional Scope types.</summary>
        internal static FixtureRequestFactory Close(
            IEntityType entityType
        )
        {
            var descriptor = NestedSetModelMapping
                .For(entityType.Model)
                .Descriptor(entityType);

            var closed = typeof(FixtureRequestFactory<,>).MakeGenericType(
                descriptor.TreeId.ClrType,
                descriptor.Scope?.ClrType ?? typeof(NestedSetNoScope));

            return (FixtureRequestFactory)Activator.CreateInstance(closed, nonPublic: true)!;
        }

        /// <summary>Reads fixture identity values at the EntityEntry metadata boundary.</summary>
        internal abstract INestedSetTreeLockRequest Create(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
            NestedSetTreeLockMode mode
        );

        /// <summary>Changes the lifecycle requirement while preserving the request's typed identity.</summary>
        internal abstract INestedSetTreeLockRequest WithMode(
            INestedSetTreeLockRequest request,
            NestedSetTreeLockMode mode
        );
    }

    /// <summary>Creates exact typed requests for the model specialization retained in the weak cache.</summary>
    private sealed class FixtureRequestFactory<TTreeId, TScope> : FixtureRequestFactory
        where TTreeId : notnull
        where TScope : notnull
    {
        /// <inheritdoc />
        internal override INestedSetTreeLockRequest Create(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
            NestedSetTreeLockMode mode
        )
        {
            var descriptor = NestedSetModelMapping
                .For(entry.Metadata.Model)
                .Descriptor(entry.Metadata);

            var scope = descriptor.Scope is { } scopeProperty
                ? (TScope)(entry.Property(scopeProperty.Name).CurrentValue
                    ?? throw new InvalidOperationException("A fixture hierarchy Scope cannot be null."))
                : default!;

            var treeId = (TTreeId)(entry.Property(descriptor.TreeId.Name).CurrentValue
                ?? throw new InvalidOperationException("A fixture hierarchy TreeId cannot be null."));

            // WHY: EntityEntry exposes erased metadata values. Cast exactly here and keep both roles typed
            // throughout request construction and subsequent lifecycle selection, including scopeless trees.
            return new NestedSetTreeLockRequest<TTreeId, TScope>(entry.Metadata, scope, treeId, mode);
        }

        /// <inheritdoc />
        internal override INestedSetTreeLockRequest WithMode(
            INestedSetTreeLockRequest request,
            NestedSetTreeLockMode mode
        )
        {
            var typed = (NestedSetTreeLockRequest<TTreeId, TScope>)request;

            return new NestedSetTreeLockRequest<TTreeId, TScope>(
                typed.Mapping.Hierarchy,
                typed.Scope,
                typed.TreeId,
                mode);
        }
    }

    /// <summary>Compares complete registry identities by their provider values.</summary>
    private sealed class RegistryRequestComparer : IEqualityComparer<INestedSetTreeLockRequest>
    {
        internal static readonly RegistryRequestComparer Instance = new();

        /// <inheritdoc />
        public bool Equals(
            INestedSetTreeLockRequest? first,
            INestedSetTreeLockRequest? second
        )
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }

            if (first is null
                || second is null
                || !ReferenceEquals(first.Mapping.Hierarchy, second.Mapping.Hierarchy))
            {
                return false;
            }

            var scopeMatches = first.Mapping.SourceScope is null
                || NestedSetProviderComparer.Matches(first.Mapping.SourceScope, first.ScopeValue, second.ScopeValue);

            return scopeMatches
                && NestedSetProviderComparer.Matches(first.Mapping.SourceTreeId, first.TreeIdValue, second.TreeIdValue);
        }

        /// <inheritdoc />
        public int GetHashCode(
            INestedSetTreeLockRequest request
        )
        {
            var hash = new HashCode();
            hash.Add(request.Mapping.Hierarchy);

            if (request.Mapping.SourceScope is { } scope)
            {
                hash.Add(NestedSetProviderComparer.GetHashCode(scope, request.ScopeValue!));
            }

            hash.Add(NestedSetProviderComparer.GetHashCode(request.Mapping.SourceTreeId, request.TreeIdValue));

            return hash.ToHashCode();
        }
    }
}
