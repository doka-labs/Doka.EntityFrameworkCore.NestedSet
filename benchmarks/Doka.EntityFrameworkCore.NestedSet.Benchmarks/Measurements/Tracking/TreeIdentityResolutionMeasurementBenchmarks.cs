using System.Collections;
using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Facade;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes ordered production anchor resolution with independently materialized unrelated rows.</summary>
public class TreeIdentityResolutionMeasurementBenchmarks
{
    private IList _rows = null!;
    private readonly Guid _source = new("74100000-0000-0000-0000-000000000001");
    private readonly Guid _target = new("74100000-0000-0000-0000-000000000002");

    /// <summary>Gets or sets the original number of unrelated ordinal rows surrounding requested anchors.</summary>
    [Params(0, 63, 2048)]
    public int IgnoredRows { get; set; }

    /// <summary>Constructs the exact production row type and binding outside measured resolution.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _rows = Probe<Guid>.Rows(
        [
            .. Enumerable.Range(0, IgnoredRows).Select(index => (index + 2, Guid.Empty)),
            (1, _target),
            (0, _source),
        ]);
        VerifyWorkload();
    }

    /// <summary>Resolves the already materialized rows through the production selector.</summary>
    /// <returns>Source then destination, regardless of the observed row ordering.</returns>
    [Benchmark]
    public (Guid Source, Guid Target) Resolve() => Probe<Guid>.Resolve(_rows);

    /// <summary>Retains requested order and complete row membership without a byte-count threshold.</summary>
    [GlobalCleanup]
    public void VerifyWorkload()
    {
        if (Resolve() != (_source, _target) || _rows.Count != IgnoredRows + 2)
        {
            throw new InvalidOperationException("The production tree-identity resolution workload changed.");
        }
    }

    /// <summary>
    /// Binds the production private selector once while retaining its actual generic row representation.
    /// </summary>
    /// <typeparam name="TTreeId">The tree identity type whose storage must remain unchanged.</typeparam>
    private static class Probe<TTreeId>
        where TTreeId : notnull
    {
        private static readonly Type s_rows;
        private static readonly Func<int, TTreeId, object> s_row;

        /// <summary>The direct compiled call to the actual production selector.</summary>
        internal static readonly Func<object, (TTreeId Source, TTreeId Target)> Resolve;

        static Probe()
        {
            var invoker = typeof(NestedSetMutationInvoker<TrackingHierarchy.Node, int, TTreeId, int>);
            var method = invoker.GetMethod("ResolveTreeIds", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("The production tree identity selector was not found.");

            s_rows = method
                .GetParameters()[0].ParameterType;

            var row = s_rows
                .GetGenericArguments()[0];

            var constructor = row.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null)!;

            var ordinal = Expression.Parameter(typeof(int), "ordinal");
            var tree = Expression.Parameter(typeof(TTreeId), "tree");
            var value = Expression.MemberInit(
                Expression.New(constructor),
                Expression.Bind(row.GetProperty("Ordinal")!, ordinal),
                Expression.Bind(row.GetProperty("TreeId")!, tree));

            s_row = Expression
                .Lambda<Func<int, TTreeId, object>>(Expression.Convert(value, typeof(object)), ordinal, tree)
                .Compile();

            var rows = Expression.Parameter(typeof(object), "rows");
            Resolve = Expression
                .Lambda<Func<object, (TTreeId Source, TTreeId Target)>>(
                    Expression.Call(method, Expression.Convert(rows, s_rows)),
                    rows)
                .Compile();
        }

        /// <summary>Constructs the exact private row list before any semantic or allocation observation.</summary>
        /// <param name="values">Requested row ordinals and typed tree identities.</param>
        /// <returns>The production selector's actual list, exposed only for count verification.</returns>
        internal static IList Rows(
            params (int Ordinal, TTreeId TreeId)[] values
        )
        {
            var rows = (IList)Activator.CreateInstance(s_rows)!;

            foreach (var (ordinal, treeId) in values)
            {
                rows.Add(s_row(ordinal, treeId));
            }

            return rows;
        }
    }
}
