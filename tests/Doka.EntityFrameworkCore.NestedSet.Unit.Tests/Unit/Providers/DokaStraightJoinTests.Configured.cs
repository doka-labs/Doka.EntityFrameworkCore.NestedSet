using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaStraightJoinTests
{
    /// <summary>
    /// Each configured engine uses only its actual public contract without creating a database command.
    /// </summary>
    /// <param name="engine">The explicitly configured Doka engine.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task ConfiguredProviderUsesItsActualMethodWithoutIo(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionGuardContext>(
                ModelCompatibilityDatabase.Options<CompositionGuardContext>(engine,
                    "Server=127.0.0.1;Database=straight_join_contract;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: Dialect probes own their service graphs rather than changing process-global
        // provider cache cardinality for unrelated model and sample regressions.
        await using var provider = services.BuildServiceProvider();
        var isolated = new DbContextOptionsBuilder<CompositionGuardContext>(options)
            .UseInternalServiceProvider(provider).Options;

        await using var context = new CompositionGuardContext(isolated);
        var adapter = NestedSetProviderCapabilities.Resolve(context);
        Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>,
            Expression<Func<CompositionGuardNode, int>>, Expression<Func<CompositionGuardNode, int>>,
            Expression<Func<CompositionGuardNode, CompositionGuardNode, int>>, IQueryable<int>> publicApi =
            MySqlQueryableExtensions.StraightJoin;

        var outer = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping).Where(node => node.Id > 4);
        var inner = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping).Where(node => node.Id < 12);

        // Act
        var available = adapter.TryOrderedJoin(outer, inner,
            node => node.Id, node => node.Id, (left, right) => new { Left = left.Id, Right = right.Id },
            out var joined);

        var method = adapter.OrderedJoinMethod;
        var sql = joined?.ToQueryString();

        // Assert
        Assert.True(available);
        Assert.Equal(publicApi.Method.GetGenericMethodDefinition(), method);
        Assert.Equal(available, joined is not null);
        Assert.Equal(available, sql is not null);

        if (available)
        {
            Assert.Contains("STRAIGHT_JOIN", sql!, StringComparison.Ordinal);
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Other selected adapters retain the portable path before any query execution.</summary>
    [Fact]
    public void NonDokaAdapterKeepsPortableJoinBeforeIo()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var source = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var adapter = NestedSetProviderCapabilities.Resolve(context);

        // Act
        var available = adapter.TryOrderedJoin(source, source, node => node.Id, node => node.Id,
            (left, right) => left.Id + right.Id, out var query);

        // Assert
        Assert.False(available);
        Assert.Null(query);
        Assert.Null(adapter.OrderedJoinMethod);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Foreign EF providers reject the public Doka operator during translation, before database I/O.</summary>
    /// <param name="asynchronous">Whether execution uses the asynchronous EF entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicDokaOperatorCannotExecuteThroughSqlite(bool asynchronous)
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine("MySql");
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        adapter.TryOrderedJoin(nodes, nodes, node => node.Id, node => node.Id,
            (left, right) => left.Id + right.Id, out var query);

        // Act
        var error = asynchronous
            ? await Record.ExceptionAsync(() => query!.ToArrayAsync(CancellationToken.None))
            : Record.Exception(() => query!.ToQueryString());

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("StraightJoin", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Every required input is validated before any configured provider lookup or enumeration.</summary>
    /// <param name="argument">The required argument deliberately omitted by this case.</param>
    [Theory]
    [InlineData("outer")]
    [InlineData("inner")]
    [InlineData("outerKey")]
    [InlineData("innerKey")]
    [InlineData("projection")]
    public void NullInputsAreRejectedAtTheBoundary(string argument)
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine("MySql");
        var source = new ProviderQueryProbe().Source<int>();
        Expression<Func<int, int>> key = value => value;
        Expression<Func<int, int, int>> projection = (left, right) => left + right;

        // Act
        var error = Record.Exception(() => adapter.TryOrderedJoin(
            argument == "outer" ? null! : source,
            argument == "inner" ? null! : source,
            argument == "outerKey" ? null! : key,
            argument == "innerKey" ? null! : key,
            argument == "projection" ? null! : projection,
            out _));

        // Assert
        Assert.IsType<ArgumentNullException>(error);
    }
}
