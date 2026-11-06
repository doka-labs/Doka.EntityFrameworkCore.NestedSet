using System.Collections;
using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Facade;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects ordered anchor resolution and its missing-before-ambiguous error contract.</summary>

// WHY: Allocation observations share the existing isolated collection rather than competing with other tests.
[Collection("Allocation measurements")]
public sealed class TreeIdentityResolutionTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Captures allocation observations without logging inside the measured interval.</summary>
    /// <param name="output">The current test's output sink.</param>
    public TreeIdentityResolutionTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Provides absent, duplicate and mixed invalid source/target rows.</summary>
    public static TheoryData<int[], NestedSetErrorCode> InvalidRows =>
        new()
        {
            { [], NestedSetErrorCode.NodeNotFound },
            { [0], NestedSetErrorCode.NodeNotFound },
            { [1], NestedSetErrorCode.NodeNotFound },
            { [-1, 2], NestedSetErrorCode.NodeNotFound },
            { [0, 0], NestedSetErrorCode.NodeNotFound },
            { [1, 1], NestedSetErrorCode.NodeNotFound },
            { [0, 0, 1], NestedSetErrorCode.InvalidStructure },
            { [0, 1, 1], NestedSetErrorCode.InvalidStructure },
            { [0, 1, 0, 1], NestedSetErrorCode.InvalidStructure },
        };

    /// <summary>Returns source then target regardless of row order, ignored ordinals or equal identities.</summary>
    /// <param name="targetFirst">Whether the target row precedes the source.</param>
    /// <param name="sameTree">Whether both anchors refer to the same tree.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UniqueAnchorsRetainRequestedOrder(
        bool targetFirst,
        bool sameTree
    )
    {
        // Arrange
        var source = Guid.NewGuid();
        var target = sameTree
            ? source
            : Guid.NewGuid();

        (int Ordinal, Guid TreeId)[] selected = targetFirst
            ? [(1, target), (0, source)]
            : [(0, source), (1, target)];

        var rows = Probe<Guid>.Rows([(-1, Guid.NewGuid()), .. selected, (2, Guid.NewGuid())]);

        // Act
        var resolved = Probe<Guid>.Resolve(rows);

        // Assert
        Assert.Equal(source, resolved.Source);
        Assert.Equal(target, resolved.Target);
        Assert.Equal(4, rows.Count);
    }

    /// <summary>Retains reference-typed tree identities without converting or replacing them.</summary>
    [Fact]
    public void ReferenceTreeIdentitiesRetainTheirInstances()
    {
        // Arrange
        var source = new string('a', 4);
        var target = new string('b', 4);
        var rows = Probe<string>.Rows((1, target), (0, source));

        // Act
        var resolved = Probe<string>.Resolve(rows);

        // Assert
        Assert.Same(source, resolved.Source);
        Assert.Same(target, resolved.Target);
    }

    /// <summary>Preserves missing-anchor precedence even when the other anchor is ambiguous.</summary>
    /// <param name="ordinals">The observed row ordinals.</param>
    /// <param name="expected">The existing public error classification.</param>
    [Theory]
    [MemberData(nameof(InvalidRows))]
    public void InvalidCardinalitiesRetainTheirError(
        int[] ordinals,
        NestedSetErrorCode expected
    )
    {
        // Arrange
        var rows = Probe<int>.Rows(
            ordinals
                .Select((ordinal, index) => (ordinal, index + 1))
                .ToArray());

        // Act
        var error = Record.Exception(() => Probe<int>.Resolve(rows));

        // Assert
        var failure = Assert.IsType<NestedSetException>(error);
        Assert.Equal(expected, failure.Code);
        Assert.Equal(
            "Source and destination must each resolve to exactly one hierarchy row in the selected Scope.",
            failure.Message);

        Assert.Equal(ordinals.Length, rows.Count);
    }

    /// <summary>Does not recreate filtered row arrays during repeated successful resolution.</summary>
    /// <param name="ignoredRows">Additional unrelated ordinals surrounding the two requested anchors.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(2_048)]
    public void SuccessfulResolutionAvoidsPerCallBuffers(
        int ignoredRows
    )
    {
        // Arrange
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var rows = Probe<Guid>.Rows(
        [
            .. Enumerable
                .Range(0, ignoredRows)
                .Select(index => (index + 2, Guid.NewGuid())),
            (1, target),
            (0, source),
        ]);

        const int repetitions = 1_024;

        // WHY: Warm the exact prepared delegate before the allocation interval; reflection and row setup are excluded.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < 128; index++)
        {
            _ = Probe<Guid>.Resolve(rows);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var resolved = default((Guid Source, Guid Target));

        // Act
        // WHY: Repeated calls expose per-resolution buffers over one unchanged materialized row list.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            resolved = Probe<Guid>.Resolve(rows);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        _output.WriteLine(
            $"Tree identity resolution: ignored={ignoredRows}; operations={repetitions}; "
            + $"allocated={allocated} B.");

        Assert.Equal(source, resolved.Source);
        Assert.Equal(target, resolved.Target);

        // WHY: A fixed noise allowance still rejects the former two filtered arrays on every invocation.
        Assert.InRange(allocated, 0, 4_096);
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
            var invoker = typeof(NestedSetMutationInvoker<TreeNode, int, TTreeId, int>);
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
