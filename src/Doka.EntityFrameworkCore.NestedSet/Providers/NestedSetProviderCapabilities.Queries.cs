using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.NestedSet.Providers;

internal abstract partial class NestedSetProviderCapabilities
{
    /// <summary>Gets whether selected anchors use the qualified independent keyset boundary.</summary>
    internal virtual bool UsesQueryAnchorKeyset => false;

    /// <summary>Gets whether strict ancestors retain an independent coordinate-only owner relation.</summary>
    internal virtual bool UsesIndependentAncestorOwners => false;

    /// <summary>Gets whether nearest-candidate lookup projects its decision before limiting the relation.</summary>
    internal virtual bool UsesProjectedQueryDecision => false;

    /// <summary>Gets whether row-independent path failures use a separate structural relation.</summary>
    internal virtual bool UsesQueryFailureRelation => false;

    /// <summary>Gets whether row-independent nearest lookup uses shared coordinate partitions.</summary>
    internal virtual bool UsesPartitionedNearestDecision => false;

    /// <summary>Gets whether correlated string scopes require the native comparison marker.</summary>
    internal virtual bool UsesNativeScopeComparison => false;

    /// <summary>Gets the exact completed-source operator trusted by endpoint provenance, when supported.</summary>
    internal virtual MethodInfo? CompletedSourceMethod => null;

    /// <summary>Gets the exact ordered-join operator trusted by endpoint provenance, when supported.</summary>
    internal virtual MethodInfo? OrderedJoinMethod => null;

    /// <summary>Selects the qualified physical subtree shape for the current configured database.</summary>
    /// <param name="context">The context whose options determine the physical strategy.</param>
    /// <returns>The provider-independent structural range join.</returns>
    internal virtual NestedSetSubtreeQueryStrategy ResolveSubtreeQueryStrategy(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        return NestedSetSubtreeQueryStrategy.RangeJoin;
    }

    /// <summary>Determines whether ancestor matches need completion before mapped owner hydration.</summary>
    /// <param name="context">The context whose current configured database determines the policy.</param>
    /// <returns>Whether a separate completed identity relation is required.</returns>
    internal virtual bool RequiresCompletedAncestorMatches(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        return false;
    }

    /// <summary>Checks whether this adapter has qualified native collection transport for the mapped key.</summary>
    /// <param name="key">The exact mapped property, including its effective converter.</param>
    /// <returns>Whether native transport preserves the qualified key representation.</returns>
    internal abstract bool SupportsTrackedKeyCollection(
        IProperty key
    );

    /// <summary>Matches captured unique keys through the provider's native parameterized collection transport.</summary>
    /// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
    /// <typeparam name="TKey">The exact native key type.</typeparam>
    /// <param name="source">The already scope- and interval-restricted entity query.</param>
    /// <param name="keyProperty">The mapped key whose native equality has been established.</param>
    /// <param name="uniqueKeys">Keys unique under that native equality until query enumeration completes.</param>
    /// <returns>A deferred query restricted to matching native keys.</returns>
    internal virtual IQueryable<TEntity> MatchTrackedKeyCollection<TEntity, TKey>(
        IQueryable<TEntity> source,
        IProperty keyProperty,
        TKey[] uniqueKeys
    )
        where TEntity : class
        where TKey : notnull
    {
        var keyName = keyProperty.Name;

        // WHY: Explicit Enumerable binding keeps C# 14 from inserting a span call into the query expression.
        return source.Where(node => Enumerable.Contains(EF.Parameter(uniqueKeys), EF.Property<TKey>(node, keyName)));
    }

    /// <summary>Composes one shared completed source when the adapter provides that physical primitive.</summary>
    /// <typeparam name="TSource">The completed source row type.</typeparam>
    /// <typeparam name="TResult">The composed result row type.</typeparam>
    /// <param name="source">The deferred source with its current captured values and occurrence semantics.</param>
    /// <param name="compose">The statement-local composition over the completed source.</param>
    /// <param name="bound">The composed query, or null when this adapter uses the portable strategy.</param>
    /// <returns>Whether the adapter provides the completed-source primitive.</returns>
    internal virtual bool TryBindSource<TSource, TResult>(
        IQueryable<TSource> source,
        Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose,
        [NotNullWhen(true)] out IQueryable<TResult>? bound
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(compose);
        bound = null;

        return false;
    }

    /// <summary>Composes one ordered join edge when the adapter has qualified that physical primitive.</summary>
    /// <typeparam name="TOuter">The outer source row type.</typeparam>
    /// <typeparam name="TInner">The inner source row type.</typeparam>
    /// <typeparam name="TKey">The common native join-key type.</typeparam>
    /// <typeparam name="TResult">The caller's projected result type.</typeparam>
    /// <param name="outer">The deferred source required before the inner lookup.</param>
    /// <param name="inner">The deferred source using the same actual query provider.</param>
    /// <param name="outerKey">The outer native key selector.</param>
    /// <param name="innerKey">The corresponding inner native key selector.</param>
    /// <param name="projection">The occurrence-preserving result projection.</param>
    /// <param name="joined">The composed query, or null when the primitive is unavailable.</param>
    /// <returns>Whether the adapter provides the ordered-join primitive.</returns>
    internal virtual bool TryOrderedJoin<TOuter, TInner, TKey, TResult>(
        IQueryable<TOuter> outer,
        IQueryable<TInner> inner,
        Expression<Func<TOuter, TKey>> outerKey,
        Expression<Func<TInner, TKey>> innerKey,
        Expression<Func<TOuter, TInner, TResult>> projection,
        [NotNullWhen(true)] out IQueryable<TResult>? joined
    )
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(outerKey);
        ArgumentNullException.ThrowIfNull(innerKey);
        ArgumentNullException.ThrowIfNull(projection);
        joined = null;

        return false;
    }
}
