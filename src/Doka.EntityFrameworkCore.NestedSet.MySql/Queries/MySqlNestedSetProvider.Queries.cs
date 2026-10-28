using System.Diagnostics.CodeAnalysis;
using Doka.EntityFrameworkCore.MySql;

namespace Doka.EntityFrameworkCore.NestedSet.MySql;

internal sealed partial class MySqlNestedSetProvider
{
    // WHY: Compile-time method groups bind the published provider contract and its actual assembly.
    // Only open metadata identities are shared; no context, request, model, or query is retained.
    private static readonly MethodInfo s_completedSource = ResolveCompletedSourceMethod();
    private static readonly MethodInfo s_orderedJoin = ResolveOrderedJoinMethod();

    /// <inheritdoc />
    internal override bool UsesIndependentAncestorOwners => true;

    /// <inheritdoc />
    internal override NestedSetNearestQueryStrategy NearestQueryStrategy => NestedSetNearestQueryStrategy.ProjectedLeft;

    /// <inheritdoc />
    internal override bool UsesQueryFailureRelation => true;

    /// <inheritdoc />
    internal override bool UsesPartitionedNearestDecision => true;

    /// <inheritdoc />
    internal override bool UsesNativeScopeComparison => true;

    /// <inheritdoc />
    internal override MethodInfo CompletedSourceMethod => s_completedSource;

    /// <inheritdoc />
    internal override MethodInfo OrderedJoinMethod => s_orderedJoin;

    /// <inheritdoc />
    internal override NestedSetSubtreeQueryStrategy ResolveSubtreeQueryStrategy(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        // WHY: Server family belongs to the current context options, including supplied/compiled models.
        // Caching it on this shared adapter would select another context's physical query strategy.
        return context.Database.IsMariaDb()
            ? NestedSetSubtreeQueryStrategy.SelectedKeyUnion
            : NestedSetSubtreeQueryStrategy.RelatedFirstUnion;
    }

    /// <inheritdoc />
    internal override bool RequiresCompletedAncestorMatches(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        return !context.Database.IsMariaDb();
    }

    /// <inheritdoc />
    internal override bool SupportsTrackedKeyCollection(
        IProperty key
    )
    {
        if (key.GetTypeMapping()
                .Converter is not null)
        {
            return false;
        }

        var type = key.ClrType;

        // WHY: Only these unconverted native representations passed the MySQL/MariaDB boundary matrix.
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(Guid);
    }

    /// <inheritdoc />
    internal override IQueryable<TEntity> MatchTrackedKeyCollection<TEntity, TKey>(
        IQueryable<TEntity> source,
        IProperty keyProperty,
        TKey[] uniqueKeys
    )
    {
        var keyName = keyProperty.Name;

        // WHY: A genuine JSON_TABLE equality join allows indexed key lookup. Membership uses
        // JSON_CONTAINS and repeatedly scans dense collections. Keep AsQueryable in the selector
        // so the provider recognizes the captured collection as a native parameter query root.
        return source
            .SelectMany(
                _ => EF
                    .Parameter(uniqueKeys)
                    .AsQueryable(),
                (node, key) => new
                {
                    Node = node,
                    Key = key,
                })
            .Where(pair => pair.Key.Equals(EF.Property<TKey>(pair.Node, keyName)))
            .Select(pair => pair.Node);
    }

    /// <inheritdoc />
    internal override bool TryBindSource<TSource, TResult>(
        IQueryable<TSource> source,
        Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose,
        [NotNullWhen(true)] out IQueryable<TResult>? bound
    )
    {
        bound = source.WithMaterializedSource(compose);

        return true;
    }

    /// <inheritdoc />
    internal override bool TryOrderedJoin<TOuter, TInner, TKey, TResult>(
        IQueryable<TOuter> outer,
        IQueryable<TInner> inner,
        Expression<Func<TOuter, TKey>> outerKey,
        Expression<Func<TInner, TKey>> innerKey,
        Expression<Func<TOuter, TInner, TResult>> projection,
        [NotNullWhen(true)] out IQueryable<TResult>? joined
    )
    {
        joined = outer.StraightJoin(inner, outerKey, innerKey, projection);

        return true;
    }

    /// <inheritdoc />
    internal override void AddServices(
        IServiceCollection services
    )
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IMethodCallTranslatorPlugin, NestedSetMySqlMethodCallTranslatorPlugin>());
    }

    /// <summary>Captures the exact public completed-source definition through a compiled method group.</summary>
    private static MethodInfo ResolveCompletedSourceMethod()
    {
        var method = MySqlQueryableExtensions.WithMaterializedSource<object, object>;

        return method.Method.GetGenericMethodDefinition();
    }

    /// <summary>Captures the exact public ordered-join definition through a compiled method group.</summary>
    private static MethodInfo ResolveOrderedJoinMethod()
    {
        var method = MySqlQueryableExtensions.StraightJoin<object, object, int, object>;

        return method.Method.GetGenericMethodDefinition();
    }
}
