using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaSourceBindingTests
{
    /// <summary>Configured engines bind only their actual typed public API and never open a connection.</summary>
    /// <param name="engine">The explicitly configured Doka engine.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task ConfiguredProviderUsesItsActualPublicIdentityWithoutIo(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionGuardContext>(
                ModelCompatibilityDatabase.Options<CompositionGuardContext>(engine,
                    "Server=127.0.0.1;Database=source_binding_contract;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: A feature probe owns its service graph so differing configured dialects do not change
        // process-global provider cache cardinality for unrelated regression or sample tests.
        await using var provider = services.BuildServiceProvider();
        var isolated = new DbContextOptionsBuilder<CompositionGuardContext>(options)
            .UseInternalServiceProvider(provider)
            .Options;

        await using var context = new CompositionGuardContext(isolated);
        var adapter = NestedSetProviderCapabilities.Resolve(context);
        Func<IQueryable<int>, Expression<Func<IQueryable<int>, IQueryable<int>>>, IQueryable<int>> publicApi =
            MySqlQueryableExtensions.WithMaterializedSource;

        var source = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping)
            .Where(node => node.Id > 4).Select(node => node.Id);

        Expression<Func<IQueryable<int>, IQueryable<int>>> compose = rows => rows.Where(value => value < 12);

        // Act
        var available = adapter.TryBindSource(source, compose, out var bound);
        var method = adapter.CompletedSourceMethod;
        var sql = bound?.ToQueryString();

        // Assert
        Assert.True(available);
        Assert.Equal(publicApi.Method.GetGenericMethodDefinition(), method);
        Assert.Equal(available, bound is not null);
        Assert.Equal(available, sql is not null);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Other selected adapters keep the portable path without borrowing Doka's marker.</summary>
    [Fact]
    public void NonDokaAdapterKeepsPortableSourceBeforeQueryExecution()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var source = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var adapter = NestedSetProviderCapabilities.Resolve(context);

        // Act
        var available = adapter.TryBindSource(source, rows => rows, out var bound);

        // Assert
        Assert.False(available);
        Assert.Null(bound);
        Assert.Null(adapter.CompletedSourceMethod);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }
}
