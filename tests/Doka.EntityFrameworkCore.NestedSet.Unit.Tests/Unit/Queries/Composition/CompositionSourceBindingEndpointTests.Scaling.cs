using System.Collections;
using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionSourceBindingEndpointTests
{
    /// <summary>Repeated lexical references retain linear-sized metadata work instead of expanding their logical bag.</summary>
    /// <param name="depth">The number of nested statement-local source definitions.</param>
    /// <param name="maximumBytes">The metadata-allocation budget; result rows are not enumerated.</param>
    [Trait("Category", "Allocation")]
    [Theory]
    [InlineData(12, 65_536)]
    [InlineData(24, 131_072)]
    [InlineData(48, 262_144)]
    public void SharedSourceDagDoesNotExpandEndpointResolution(int depth, long maximumBytes)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var query = nodes;

        for (var index = 0; index < depth; index++)
        {
            query = Bind(query, selected => selected.Concat(selected));
        }

        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        _ = NestedSetQueryEndpoint.Require(query, node => node, mapping, nodes.Expression, "node", s_binding);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = NestedSetQueryEndpoint.Require(query, node => node, mapping, nodes.Expression, "node", s_binding);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Shared-source depth={depth}; allocated={allocated} B; budget={maximumBytes} B.");

        // Assert
        Assert.Same(mapping, actual.EntityType);
        Assert.True(actual.IsVisible);
        Assert.InRange(allocated, 0, maximumBytes);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Equivalent member paths through different lambda parameters do not expand a shared input.</summary>
    /// <param name="projected">Whether a member path, rather than the whole row, identifies the endpoint.</param>
    [Trait("Category", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentIdentitySelectorsShareTheLexicalResolution(bool projected)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var query = nodes.Select(node => new { Node = node });

        for (var index = 0; index < 24; index++)
        {
            query = projected
                ? Bind(query, selected => selected.Select(x => new { Node = x.Node })
                    .Concat(selected.Select(y => new { Node = y.Node })))
                : Bind(query, selected => selected.Select(x => x).Concat(selected.Select(y => y)));
        }

        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        _ = NestedSetQueryEndpoint.Require(query, row => row.Node, mapping, nodes.Expression, "node", s_binding);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = NestedSetQueryEndpoint.Require(query, row => row.Node, mapping, nodes.Expression, "node", s_binding);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Independent selectors projected={projected}; depth=24; allocated={allocated} B.");

        // Assert
        Assert.Same(mapping, actual.EntityType);
        Assert.True(actual.IsVisible);
        Assert.InRange(allocated, 0, 262_144);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Alternating endpoint members remain separately memoized and cannot borrow a named mapping.</summary>
    /// <param name="foreignMapping">Whether the second endpoint belongs to an independently named hierarchy.</param>
    [Trait("Category", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternatingEndpointPathsPreserveTheirMapping(bool foreignMapping)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = foreignMapping
            ? context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping)
            : first;

        var query = first.Join(second, left => left.Id, right => right.Id,
            (left, right) => new CompositionGuardRow { Upper = left, Lower = right });

        for (var index = 0; index < 24; index++)
        {
            query = Bind(query, selected => selected.Select(row => new CompositionGuardRow
                {
                    Upper = row.Lower,
                    Lower = row.Upper,
                })
                .Concat(selected.Select(row => row)));
        }

        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        NestedSetQueryEndpoint.Resolution? result = null;

        // WHY: This budget measures steady-state construction like the other shared-source guards.
        // One complete call warms CLR metadata; the measured call still owns a fresh memo and proof.
        var firstCallBefore = GC.GetAllocatedBytesForCurrentThread();
        _ = Record.Exception(() => NestedSetQueryEndpoint.Require(
            query, row => row.Upper, mapping, first.Expression, "node", s_binding));

        var firstCallAllocated = GC.GetAllocatedBytesForCurrentThread() - firstCallBefore;

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = Record.Exception(() => result = NestedSetQueryEndpoint.Require(
            query, row => row.Upper, mapping, first.Expression, "node", s_binding));

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Alternating endpoints foreignMapping={foreignMapping}; depth=24; allocated={allocated} B.");
        output.WriteLine($"First complete guard call allocated={firstCallAllocated} B; measurement owns a fresh operation cache.");

        // Assert
        if (foreignMapping)
        {
            Assert.IsType<ArgumentException>(error);
        }
        else
        {
            Assert.Null(error);
            Assert.Same(mapping, result?.EntityType);
        }

        Assert.InRange(allocated, 0, 262_144);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A mutable captured query still resolves each occurrence and cannot borrow a cached mapping.</summary>
    /// <param name="throughObjectField">Whether the captured field hides the query behind its declared object type.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutableQueryCaptureDoesNotReuseAnEarlierMapping(bool throughObjectField)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        IQueryable<CompositionGuardNode> second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var changing = new ChangingQuery(first, second);
        var expression = throughObjectField
            ? Expression.Convert(Expression.Field(Expression.Constant(new ObjectQueryCapture(changing)),
                nameof(ObjectQueryCapture.Query)), typeof(IQueryable<CompositionGuardNode>))
            : (Expression)Expression.Constant(changing, typeof(IQueryable<CompositionGuardNode>));

        var captured = first.Provider.CreateQuery<CompositionGuardNode>(expression);

        var query = Bind(captured, selected => selected.Concat(selected));
        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var error = Record.Exception(() => NestedSetQueryEndpoint.Require(
            query, node => node, mapping, first.Expression, "node", s_binding));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(2, changing.Reads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Ordinary captured object payloads preserve bounded shared-source endpoint work.</summary>
    [Fact]
    public void StableObjectPayloadRetainsSharedSourceReuse()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var payload = new object();
        var query = nodes.Select(node => new { Node = node, Payload = payload });

        for (var index = 0; index < 16; index++)
        {
            query = Bind(query, selected => selected.Select(x => new { x.Node, x.Payload })
                .Concat(selected.Select(y => new { y.Node, y.Payload })));
        }

        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        _ = NestedSetQueryEndpoint.Require(query, row => row.Node, mapping, nodes.Expression, "node", s_binding);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = NestedSetQueryEndpoint.Require(query, row => row.Node, mapping, nodes.Expression, "node", s_binding);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Stable object payload; shared-source depth=16; allocated={allocated} B.");

        // Assert
        Assert.Same(mapping, actual.EntityType);
        Assert.True(actual.IsVisible);
        Assert.InRange(allocated, 0, 196_608);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Hides a captured query's runtime identity behind an ordinary object-valued field.</summary>
    private sealed class ObjectQueryCapture(object query)
    {
        /// <summary>Retains the caller's query without exposing IQueryable in the field's declared type.</summary>
        public readonly object Query = query;

        /// <summary>Retains an opaque static capture for serialized cases in this test class.</summary>
        public static object? StaticQuery;

        /// <summary>Gets the number of application getter invocations during metadata inspection.</summary>
        internal int GetterReads { get; private set; }

        /// <summary>Exposes an opaque query without allowing the endpoint guard to execute this getter.</summary>
        public object QueryProperty
        {
            get
            {
                GetterReads++;

                return Query;
            }
        }
    }

    /// <summary>Exposes application-owned expression metadata that deliberately changes between references.</summary>
    private sealed class ChangingQuery(IQueryable<CompositionGuardNode> first, IQueryable<CompositionGuardNode> second)
        : IQueryable<CompositionGuardNode>
    {
        internal int Reads { get; private set; }

        /// <inheritdoc />
        public Type ElementType => typeof(CompositionGuardNode);

        /// <inheritdoc />
        public IQueryProvider Provider => first.Provider;

        /// <inheritdoc />
        public Expression Expression => ++Reads == 1 ? first.Expression : second.Expression;

        /// <inheritdoc />
        public IEnumerator<CompositionGuardNode> GetEnumerator()
            => throw new InvalidOperationException("Endpoint provenance must not enumerate a captured query.");

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
