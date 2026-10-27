using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Releases actual typed operator inputs before their weak ownership is inspected.</summary>
internal static class ProviderQueryLifetime
{
    /// <summary>Retains only weak observations after a completed-source or ordered-join construction.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference[] ReleaseInputs(bool orderedJoin)
    {
        var probe = new ProviderQueryProbe();
        var source = probe.Source<int>();
        var request = new RequestValues { Minimum = 3 };
        var adapter = NestedSetTestAdapters.ForEngine("MySql");
        IQueryable<int>? query;
        object selector;

        if (orderedJoin)
        {
            Expression<Func<int, int>> key = value => value + request.Minimum;
            Expression<Func<int, int, int>> projection = (left, right) => left + right;
            adapter.TryOrderedJoin(source, source, key, key, projection, out query);
            selector = key;
        }
        else
        {
            Expression<Func<IQueryable<int>, IQueryable<int>>> compose =
                rows => rows.Where(value => value > request.Minimum);

            adapter.TryBindSource(source, compose, out query);
            selector = compose;
        }

        return [new(probe), new(source), new(request), new(selector), new(query!)];
    }

    /// <summary>Exercises closed typed provider methods with an actual collectible row identity.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference[] ReleaseCollectibleRows(bool orderedJoin, int position)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("NestedSet.TypedQueryArgument." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.RunAndCollect);

        var builder = assembly.DefineDynamicModule("Rows").DefineType("CollectibleRow", TypeAttributes.Public);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var row = builder.CreateTypeInfo()!.AsType();
        var probe = new ProviderQueryProbe();
        var compose = typeof(ProviderQueryLifetime)
            .GetMethod(nameof(ComposeTyped), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(row);

        var query = compose.Invoke(null, [probe, orderedJoin, position])!;

        // WHY: The only surviving references are weak. This probes Doka's real closed-method caches,
        // rather than the removed optional-binding cache or a synthetic replacement provider method.
        return [new(assembly), new(row), new(probe), new(query)];
    }

    /// <summary>Constructs the actual public operator after the CLR closes the collectible row type.</summary>
    private static object ComposeTyped<T>(ProviderQueryProbe probe, bool orderedJoin, int position)
    {
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        if (!orderedJoin)
        {
            if (position == 0)
            {
                adapter.TryBindSource(probe.Source<T>(), rows => rows.Select(_ => 1), out var result);

                return result!;
            }

            adapter.TryBindSource(probe.Source<int>(), rows => rows.Select(_ => default(T)!), out var other);

            return other!;
        }

        switch (position)
        {
            case 0:
                adapter.TryOrderedJoin(probe.Source<T>(), probe.Source<int>(), _ => 1, value => value,
                    (_, right) => right, out var outer);

                return outer!;
            case 1:
                adapter.TryOrderedJoin(probe.Source<int>(), probe.Source<T>(), value => value, _ => 1,
                    (left, _) => left, out var inner);

                return inner!;
            case 2:
                adapter.TryOrderedJoin(probe.Source<int>(), probe.Source<int>(), _ => default(T)!, _ => default(T)!,
                    (left, right) => left + right, out var key);

                return key!;
            case 3:
                adapter.TryOrderedJoin(probe.Source<int>(), probe.Source<int>(), value => value, value => value,
                    (_, _) => default(T)!, out var projected);

                return projected!;
            default:
                throw new ArgumentOutOfRangeException(nameof(position));
        }
    }

    /// <summary>Provides invocation-owned state whose lifetime must not be extended by operator metadata.</summary>
    private sealed class RequestValues
    {
        /// <summary>Gets or initializes the request's captured filter value.</summary>
        public int Minimum { get; init; }
    }
}
