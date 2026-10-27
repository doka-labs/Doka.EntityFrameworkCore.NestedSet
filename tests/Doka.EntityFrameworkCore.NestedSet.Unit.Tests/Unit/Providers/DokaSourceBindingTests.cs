using System.Globalization;
using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies actual typed completed-source composition without optional reflection discovery.</summary>
public sealed partial class DokaSourceBindingTests
{
    /// <summary>The actual Doka operator retains its complete source and exact deferred composition.</summary>
    [Fact]
    public void TypedOperatorRetainsItsExactSourceAndCompositionWithoutExecution()
    {
        // Arrange
        var probe = new ProviderQueryProbe();
        var source = probe.Source<int>();
        Expression<Func<IQueryable<int>, IQueryable<string>>> compose =
            rows => rows.Select(value => value.ToString(CultureInfo.InvariantCulture));
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        var available = adapter.TryBindSource(source, compose, out var bound);

        // Assert
        Assert.True(available);
        var call = Assert.IsType<MethodCallExpression>(bound!.Expression, exactMatch: false);

        Assert.Equal(adapter.CompletedSourceMethod, call.Method.GetGenericMethodDefinition());
        Assert.Equal(typeof(MySqlQueryableExtensions).Assembly, call.Method.DeclaringType!.Assembly);
        Assert.Same(source.Expression, call.Arguments[0]);
        Assert.Same(compose, Assert.IsType<UnaryExpression>(call.Arguments[1], exactMatch: false).Operand);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>Repeated typed calls cannot reuse a previous request's query or captured values.</summary>
    [Fact]
    public void RepeatedCallsRetainEachFreshDeferredCapture()
    {
        // Arrange
        var probe = new ProviderQueryProbe();
        var source = probe.Source<int>();
        var firstLimit = 2;
        var secondLimit = 7;
        Expression<Func<IQueryable<int>, IQueryable<int>>> first = rows => rows.Where(value => value > firstLimit);
        Expression<Func<IQueryable<int>, IQueryable<int>>> second = rows => rows.Where(value => value > secondLimit);
        var adapter = NestedSetTestAdapters.ForEngine("MySql");
        int[] values = [3, 5, 9];

        // Act
        adapter.TryBindSource(source, first, out var firstBound);
        adapter.TryBindSource(source, second, out var secondBound);
        firstLimit = 4;
        secondLimit = 8;
        var firstValues = Composition(firstBound!).Compile()(values.AsQueryable()).ToArray();
        var secondValues = Composition(secondBound!).Compile()(values.AsQueryable()).ToArray();

        // Assert
        Assert.NotSame(firstBound, secondBound);
        Assert.Same(first, Composition(firstBound!));
        Assert.Same(second, Composition(secondBound!));
        Assert.Equal([5, 9], firstValues);
        Assert.Equal([9], secondValues);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>Source paging and duplicate occurrences are passed to Doka unchanged.</summary>
    /// <param name="empty">Whether paging deliberately selects no occurrences.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteSourceRetainsDuplicatesAndEmptyPaging(bool empty)
    {
        // Arrange
        var probe = new ProviderQueryProbe();
        var root = probe.Source<int>();
        var source = root.Concat(root).OrderBy(value => value).Take(empty ? 0 : 4);
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        adapter.TryBindSource(source, rows => rows, out var bound);

        // Assert
        var call = Assert.IsType<MethodCallExpression>(bound!.Expression, exactMatch: false);

        Assert.Same(source.Expression, call.Arguments[0]);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>Portable adapters do not manufacture a foreign completed-source operator.</summary>
    /// <param name="engine">The adapter whose portable query path is retained.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void PortableAdaptersLeaveCompletedSourceUnavailable(string engine)
    {
        // Arrange
        var probe = new ProviderQueryProbe();
        var source = probe.Source<int>();
        var adapter = NestedSetTestAdapters.ForEngine(engine);

        // Act
        var available = adapter.TryBindSource(source, rows => rows, out var bound);

        // Assert
        Assert.False(available);
        Assert.Null(bound);
        Assert.Null(adapter.CompletedSourceMethod);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>The directly called public provider error remains unwrapped.</summary>
    [Fact]
    public void ClientSourceIsRejectedWithoutReflectionWrapping()
    {
        // Arrange
        var source = Array.Empty<int>().AsQueryable();
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        var error = Record.Exception(() => adapter.TryBindSource(source, rows => rows, out _));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Doka EF Core", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An actual query provider's construction failure crosses the direct call unchanged.</summary>
    [Fact]
    public void QueryProviderFailureIsNotWrapped()
    {
        // Arrange
        var expected = new InvalidOperationException("The actual query provider rejected construction.");
        var probe = new ProviderQueryProbe { ConstructionError = expected };
        var source = probe.Source<int>();
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        var error = Record.Exception(() => adapter.TryBindSource(source, rows => rows, out _));

        // Assert
        Assert.Same(expected, error);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>Required source inputs are validated by the actual typed provider boundary.</summary>
    /// <param name="sourceMissing">Whether the source rather than the composition is absent.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullInputsAreRejectedAtTheTypedBoundary(bool sourceMissing)
    {
        // Arrange
        var probe = new ProviderQueryProbe();
        var source = probe.Source<int>();
        Expression<Func<IQueryable<int>, IQueryable<int>>> compose = rows => rows;
        var adapter = NestedSetTestAdapters.ForEngine("MySql");

        // Act
        var error = Record.Exception(() => adapter.TryBindSource(
            sourceMissing ? null! : source, sourceMissing ? compose : null!, out _));

        // Assert
        Assert.Equal(sourceMissing ? "source" : "compose", Assert.IsType<ArgumentNullException>(error).ParamName);
        Assert.Equal(0, probe.Executions);
    }

    /// <summary>Reads the actual quoted user composition without executing the provider marker.</summary>
    private static Expression<Func<IQueryable<int>, IQueryable<int>>> Composition(IQueryable<int> query)
        => (Expression<Func<IQueryable<int>, IQueryable<int>>>)((UnaryExpression)((MethodCallExpression)query.Expression)
            .Arguments[1]).Operand;
}
