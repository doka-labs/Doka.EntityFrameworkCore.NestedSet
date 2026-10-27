using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the actual typed Doka ordered-join primitive and its deferred input ownership.</summary>
public sealed partial class DokaStraightJoinTests
{
    /// <summary>The direct operator preserves both inputs and all typed selectors without execution.</summary>
    [Fact]
    public void TypedOrderedJoinRetainsItsExactInputsAndSelectors()
    {
        // Arrange
        var probe = new ProviderQueryProbe();
        var outer = probe.Source<int>();
        var inner = probe.Source<string>();
        Expression<Func<int, int>> outerKey = value => value;
        Expression<Func<string, int>> innerKey = value => value.Length;
        Expression<Func<int, string, string>> projection = (left, right) => right + left;
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        var available = adapter.TryOrderedJoin(outer, inner, outerKey, innerKey, projection, out var joined);

        // Assert
        Assert.True(available);
        var call = Assert.IsType<MethodCallExpression>(joined!.Expression, exactMatch: false);

        Assert.Equal(adapter.OrderedJoinMethod, call.Method.GetGenericMethodDefinition());
        Assert.Equal(typeof(MySqlQueryableExtensions).Assembly, call.Method.DeclaringType!.Assembly);
        Assert.Same(outer.Expression, call.Arguments[0]);
        Assert.Same(inner.Expression, call.Arguments[1]);
        Assert.Same(outerKey, ((UnaryExpression)call.Arguments[2]).Operand);
        Assert.Same(innerKey, ((UnaryExpression)call.Arguments[3]).Operand);
        Assert.Same(projection, ((UnaryExpression)call.Arguments[4]).Operand);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>Repeated calls preserve each fresh capture and do not replace duplicate or empty sources.</summary>
    /// <param name="empty">Whether the second input deliberately selects no occurrences.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshCapturesAndCompleteSourceOccurrencesArePreserved(bool empty)
    {
        // Arrange
        var probe = new ProviderQueryProbe();
        var root = probe.Source<int>();
        var outer = root.Concat(root);
        var inner = root.OrderBy(value => value).Take(empty ? 0 : 4);
        var firstOffset = 2;
        var secondOffset = 7;
        Expression<Func<int, int>> firstKey = value => value + firstOffset;
        Expression<Func<int, int>> secondKey = value => value + secondOffset;
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        adapter.TryOrderedJoin(outer, inner, firstKey, firstKey, (left, right) => left + right, out var first);
        adapter.TryOrderedJoin(outer, inner, secondKey, secondKey, (left, right) => left + right, out var second);
        firstOffset = 4;
        secondOffset = 8;
        var firstSelector = KeySelector(first!);
        var secondSelector = KeySelector(second!);

        // Assert
        Assert.NotSame(first, second);
        Assert.Same(firstKey, firstSelector);
        Assert.Same(secondKey, secondSelector);
        Assert.Equal(5, firstSelector.Compile()(1));
        Assert.Equal(9, secondSelector.Compile()(1));
        Assert.Same(outer.Expression, ((MethodCallExpression)second!.Expression).Arguments[0]);
        Assert.Same(inner.Expression, ((MethodCallExpression)second.Expression).Arguments[1]);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>Portable adapters cannot manufacture an ordered-join marker belonging to another provider.</summary>
    /// <param name="engine">The adapter whose portable relational query path is retained.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void PortableAdaptersKeepOrderedJoinUnavailable(string engine)
    {
        // Arrange
        var probe = new ProviderQueryProbe();
        var source = probe.Source<int>();
        var adapter = NestedSetTestAdapters.ForEngine(engine);

        // Act
        var available = adapter.TryOrderedJoin(source, source, value => value, value => value,
            (left, right) => left + right, out var joined);

        // Assert
        Assert.False(available);
        Assert.Null(joined);
        Assert.Null(adapter.OrderedJoinMethod);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>The typed native boundary rejects client sources without reflection exception wrappers.</summary>
    [Fact]
    public void ClientSourcesAreRejectedWithoutReflectionWrapping()
    {
        // Arrange
        var source = Array.Empty<int>().AsQueryable();
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        var error = Record.Exception(() => adapter.TryOrderedJoin(source, source, value => value, value => value,
            (left, right) => left + right, out _));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("StraightJoin requires both inputs to use the same Doka EF query provider.", error.Message);
    }

    /// <summary>An actual provider exception crosses the typed ordered-join call without reflection wrapping.</summary>
    [Fact]
    public void QueryProviderFailureIsNotWrapped()
    {
        // Arrange
        var expected = new InvalidOperationException("The actual query provider rejected construction.");
        var probe = new ProviderQueryProbe { ConstructionError = expected };
        var source = probe.Source<int>();
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        var error = Record.Exception(() => adapter.TryOrderedJoin(source, source, value => value, value => value,
            (left, right) => left + right, out _));

        // Assert
        Assert.Same(expected, error);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>Reads the actual quoted outer-key selector without executing an ordered join.</summary>
    private static Expression<Func<int, int>> KeySelector(IQueryable<int> query)
        => (Expression<Func<int, int>>)((UnaryExpression)((MethodCallExpression)query.Expression).Arguments[2]).Operand;
}
